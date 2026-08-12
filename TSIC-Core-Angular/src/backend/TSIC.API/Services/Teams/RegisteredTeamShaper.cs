using TSIC.Contracts.Dtos;
using TSIC.Contracts.Extensions;
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
            paymentStates,
            job.BAddProcessingFees ?? false,
            ccRate,
            echeckRate);
    }

    private static List<RegisteredTeamDto> ShapeRegisteredTeams(
        IEnumerable<RegisteredTeamInfo> rawRegistered,
        HashSet<int> scheduledClubTeamIds,
        Dictionary<Guid, ResolvedFee> feesByTeamId,
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
            // Per-team phase: this team's JobFees override (team → ag → league); no override
            // = deposit — so a converted camp/agegroup shows balance-due math while its
            // siblings still in deposit phase show the forward-looking balance.
            var teamFullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved);

            // Per-method owed from the single canonical resolver — the SAME
            // PaymentState.ResolveOwed the charge engine (PaymentService) uses, so the
            // totals the rep is shown for CC / check / eCheck equal exactly what each
            // method charges or records (keeps the AMOUNT_MISMATCH tripwire quiet).
            var state = paymentStates.GetValueOrDefault(t.TeamId, emptyState);
            // donation: 0m — a donation is always charged in full with its payment, so it nets out
            // of these post-payment display columns. RegisteredTeamInfo doesn't carry FeeDonation;
            // threading it would change nothing here. (The charge/stamp paths pass the real FeeDonation.)
            // TotalDiscount() — both buckets, exactly what FeeMath subtracted from FeeTotal.
            var discount = t.TotalDiscount();
            var owed = state.ResolveOwed(t.OwedTotal, t.FeeBase, discount, t.FeeLatefee, donation: 0m, t.FeeProcessing);
            var ccOwedTotal = owed.Cc;
            var ckOwedTotal = owed.Check;
            var ekOwedTotal = owed.Echeck;
            var feeProcessingDue = Math.Max(0m, ccOwedTotal - ckOwedTotal);

            // Deposit Due / Balance Due anchor on the stored entity totals via ResolveOwed's
            // proc-free (Check) figure — NEVER on a principal re-derived from the ledger.
            //
            // Why: Registration_Accounting.Payamt is money-in, nothing more. The ledger has never
            // carried a per-row principal/proc split (in the legacy era OR now) — that split lives
            // only in the entity columns, maintained at write time when it was actually known.
            // The previous PrincipalRemaining/DepositPrincipalRemaining display math re-decoded
            // CC rows as gross-with-proc (payamt ÷ (1+rate)), which is only guaranteed for rows
            // the current charge engine wrote itself. Legacy club-lump allocation rows
            // ("paid by cc: $1,300.00 of $5,291.00") book flat principal; decoding them invented
            // ~3.4% of phantom "still owed" — 38 settled prod teams showed $10–57 Balance Due
            // beside Owed $0.00 (2026-08-11 sweep). CkOwedTotal is the same "principal still
            // owed if paid proc-free" quantity, but anchored on OwedTotal with the proc credit
            // capped at stored FeeProcessing — for self-consistent (new-engine) data it equals
            // the old figure; for legacy-paid teams it agrees with the books by construction.
            //
            //   • Deposit phase  → Deposit Due = remaining obligation (CkOwedTotal; the stamp set
            //     FeeBase = EffectiveDeposit, so owed IS the deposit-phase bill — including the
            //     deposit-less single-payment shape, where the whole fee shows here and Balance
            //     Due stays $0). Balance Due = the configured structural balance, forward-looking.
            //   • Full-payment phase → the deposit concept is retired: Deposit Due collapses to 0
            //     and the entire remainder (CkOwedTotal) shows as Balance Due, which therefore can
            //     never disagree with the Owed/Check Owed columns beside it.
            var depositDue = teamFullPayment ? 0m : ckOwedTotal;
            var additionalDue = teamFullPayment
                ? ckOwedTotal
                : (deposit > 0m ? balanceDue : 0m);

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
                FeeAdj = state.FeeAdjustment(discount, t.FeeLatefee),
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
