using TSIC.Contracts.Dtos;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Teams;

/// <summary>
/// Shapes raw <see cref="RegisteredTeamInfo"/> rows into the per-team financial
/// <see cref="RegisteredTeamDto"/> the registered-teams grid consumes.
///
/// This is the single place the rich per-method owed math (CC / check / eCheck owed,
/// processing-fee-due, deposit/balance-due) is derived for team grids. Both the team
/// registration wizard (rep's own payment page) and the director's club-rep accounting
/// view call through here, so the two grids can never disagree.
/// </summary>
public interface IRegisteredTeamShaper
{
    /// <param name="scheduledClubTeamIds">
    /// Pre-resolved "has ever been scheduled" club-team ids. When null, the shaper
    /// resolves them itself for the supplied teams — callers that already computed the
    /// set for a wider candidate list (e.g. the wizard, which also needs it for library
    /// teams) pass it through to avoid a second lookup.
    /// </param>
    Task<List<RegisteredTeamDto>> ShapeAsync(
        Guid jobId,
        IReadOnlyList<RegisteredTeamInfo> rawRegistered,
        HashSet<int>? scheduledClubTeamIds = null,
        CancellationToken ct = default);
}

public sealed class RegisteredTeamShaper : IRegisteredTeamShaper
{
    private readonly IJobRepository _jobs;
    private readonly IFeeResolutionService _feeService;
    private readonly IPaymentStateService _paymentState;
    private readonly IClubTeamRepository _clubTeams;

    public RegisteredTeamShaper(
        IJobRepository jobs,
        IFeeResolutionService feeService,
        IPaymentStateService paymentState,
        IClubTeamRepository clubTeams)
    {
        _jobs = jobs;
        _feeService = feeService;
        _paymentState = paymentState;
        _clubTeams = clubTeams;
    }

    public async Task<List<RegisteredTeamDto>> ShapeAsync(
        Guid jobId,
        IReadOnlyList<RegisteredTeamInfo> rawRegistered,
        HashSet<int>? scheduledClubTeamIds = null,
        CancellationToken ct = default)
    {
        if (rawRegistered.Count == 0) return [];

        var job = await _jobs.GetJobFeeSettingsAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Event not found for jobId: {jobId}");

        var teamIds = rawRegistered.Select(t => t.TeamId).ToList();

        var feesByTeamId = await _feeService.ResolveFeesByTeamIdsAsync(jobId, RoleConstants.ClubRep, teamIds);
        var paymentStates = await _paymentState.ForTeamsAsync(teamIds, jobId, ct);

        var scheduledIds = scheduledClubTeamIds ?? await _clubTeams.GetScheduledClubTeamIdsAsync(
            rawRegistered.Where(t => t.ClubTeamId.HasValue).Select(t => t.ClubTeamId!.Value).Distinct(), ct);

        var ccRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);
        var echeckRate = await _feeService.GetEffectiveEcheckProcessingRateAsync(jobId);

        return ShapeRegisteredTeams(
            rawRegistered,
            scheduledIds,
            feesByTeamId,
            job.BTeamsFullPaymentRequired ?? false,
            paymentStates,
            job.BAddProcessingFees ?? false,
            ccRate,
            echeckRate);
    }

    private static List<RegisteredTeamDto> ShapeRegisteredTeams(
        IEnumerable<RegisteredTeamInfo> rawRegistered,
        HashSet<int> scheduledClubTeamIds,
        Dictionary<Guid, ResolvedFee> feesByTeamId,
        bool jobTeamsFullPaymentBaseline,
        Dictionary<Guid, PaymentState> paymentStates,
        bool bAddProcessingFees,
        decimal ccRate,
        decimal echeckRate)
    {
        var emptyState = PaymentState.Empty(bAddProcessingFees, ccRate, echeckRate);
        return rawRegistered.Select(t =>
        {
            var resolved = feesByTeamId.GetValueOrDefault(t.TeamId);
            var deposit = resolved?.Deposit ?? 0m;
            var balanceDue = resolved?.BalanceDue ?? 0m;
            // Per-team phase: this team's JobFees override (team → ag → league) wins over
            // the job baseline — so a converted camp/agegroup shows balance-due math while
            // its siblings still in deposit phase show the forward-looking balance.
            var teamFullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved, jobTeamsFullPaymentBaseline);

            // Per-method owed from the single canonical resolver — the SAME
            // PaymentState.ResolveOwed the charge engine (PaymentService) uses, so the
            // totals the rep is shown for CC / check / eCheck equal exactly what each
            // method charges or records (keeps the AMOUNT_MISMATCH tripwire quiet).
            // Deposit-phase owed goes through the PROPORTIONAL deposit helper so the displayed
            // "Deposit Due" equals what ArbTrialFeeSplitter actually charges: a discount reduces the
            // deposit by its pro-rata SHARE (FeeBase = Deposit + BalanceDue here), not front-loaded
            // onto it. Shown-deposit and charged-deposit derive from the same proportional rule.
            var state = paymentStates.GetValueOrDefault(t.TeamId, emptyState);
            // donation: 0m — a donation is always charged in full with its payment, so it sits in
            // BOTH the principal base and PrincipalPaid and nets out of these post-payment display
            // columns. RegisteredTeamInfo doesn't carry FeeDonation; threading it would change
            // nothing here. (The charge/stamp paths pass the real FeeDonation.)
            var depositDue = state.DepositPrincipalRemainingProportional(t.FeeBase, deposit, t.FeeDiscount, t.FeeLatefee, donation: 0m);
            // Full-payment phase: the balance is active (FeeBase = Deposit + BalanceDue),
            // so "Balance Due" must net out every payment — including a director-recorded
            // check/correction applied while the rep was away. Derive it from the canonical
            // PaymentState (BalancePrincipalRemainingProportional, the proportional pair of the
            // deposit above) so it can never disagree with the CC/Check Owed columns. Deposit phase:
            // balance not yet active — show the configured structural balance as a forward-looking value.
            var additionalDue = teamFullPayment
                ? state.BalancePrincipalRemainingProportional(t.FeeBase, deposit, t.FeeDiscount, t.FeeLatefee, donation: 0m)
                : balanceDue;
            var owed = state.ResolveOwed(t.OwedTotal, t.FeeBase, t.FeeDiscount, t.FeeLatefee, donation: 0m, t.FeeProcessing);
            var ccOwedTotal = owed.Cc;
            var ckOwedTotal = owed.Check;
            var ekOwedTotal = owed.Echeck;
            var feeProcessingDue = Math.Max(0m, ccOwedTotal - ckOwedTotal);

            return new RegisteredTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                AgeGroupId = t.AgeGroupId,
                AgeGroupName = t.AgeGroupName,
                LevelOfPlay = t.LevelOfPlay,
                FeeBase = t.FeeBase,
                FeeProcessing = t.FeeProcessing,           // raw statement-of-fact
                FeeProcessingDue = feeProcessingDue,       // OwedTotal − CkOwedTotal
                FeeDiscount = t.FeeDiscount,
                FeeLatefee = t.FeeLatefee,
                FeeTotal = t.FeeTotal,
                PaidTotal = t.PaidTotal,
                OwedTotal = t.OwedTotal,
                FeeAdj = state.FeeAdjustment(t.FeeDiscount, t.FeeLatefee),
                TenderPaid = state.TenderPaid,
                Deposit = deposit,
                BalanceDue = balanceDue,
                FullPaymentRequired = teamFullPayment,
                DepositDue = depositDue,
                AdditionalDue = additionalDue,
                RegistrationTs = t.RegistrationTs,
                BWaiverSigned3 = t.BWaiverSigned3,
                CcOwedTotal = ccOwedTotal,
                CkOwedTotal = ckOwedTotal,
                EkOwedTotal = ekOwedTotal,
                ClubTeamId = t.ClubTeamId,
                BHasBeenScheduled = t.ClubTeamId.HasValue && scheduledClubTeamIds.Contains(t.ClubTeamId.Value),
                Active = t.Active,
                PaymentScheduled = t.PaymentScheduled,
                NextChargeDate = t.NextChargeDate,
            };
        }).ToList();
    }
}
