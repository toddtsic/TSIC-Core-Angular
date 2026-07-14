using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Players;

/// <summary>
/// Shapes raw <see cref="RegisteredPlayerInfo"/> rows into the per-player financial
/// <see cref="RegisteredTeamDto"/> the registered-teams grid consumes — the player-side
/// analog of <see cref="Teams.RegisteredTeamShaper"/>.
///
/// Routes every player through the SAME canonical payment-state path teams use:
/// <see cref="IPaymentStateService.ForRegistrationsAsync"/> for the per-method owed
/// (<see cref="PaymentState.ResolveOwed"/>), and <see cref="IFeeResolutionService"/> for
/// deposit/balance. FeeProcessing is READ straight off the registration record (a
/// statement-of-fact that already assumes CC and is adjusted proportionally by
/// RegistrationFeeAdjustmentService when a non-CC payment lands) — never invented here.
/// </summary>
public interface IRegisteredPlayerShaper
{
    Task<List<RegisteredTeamDto>> ShapeAsync(
        Guid jobId,
        IReadOnlyList<RegisteredPlayerInfo> rawPlayers,
        CancellationToken ct = default);
}

public sealed class RegisteredPlayerShaper : IRegisteredPlayerShaper
{
    private readonly IJobRepository _jobs;
    private readonly IFeeResolutionService _feeService;
    private readonly IPaymentStateService _paymentState;

    public RegisteredPlayerShaper(
        IJobRepository jobs,
        IFeeResolutionService feeService,
        IPaymentStateService paymentState)
    {
        _jobs = jobs;
        _feeService = feeService;
        _paymentState = paymentState;
    }

    public async Task<List<RegisteredTeamDto>> ShapeAsync(
        Guid jobId,
        IReadOnlyList<RegisteredPlayerInfo> rawPlayers,
        CancellationToken ct = default)
    {
        if (rawPlayers.Count == 0) return [];

        var job = await _jobs.GetJobFeeSettingsAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Event not found for jobId: {jobId}");

        // Per-method owed comes from the canonical payment state — the player analog of the
        // team shaper's _paymentState.ForTeamsAsync. One batch for the whole family.
        var regIds = rawPlayers.Select(p => p.RegistrationId).ToList();
        var paymentStates = await _paymentState.ForRegistrationsAsync(regIds, jobId, ct);

        // Deposit/balance resolve from the fee cascade by the player's assigned team — the
        // player analog of the team shaper's ResolveFeesByTeamIdsAsync.
        var teamIds = rawPlayers.Where(p => p.AssignedTeamId.HasValue)
            .Select(p => p.AssignedTeamId!.Value).Distinct().ToList();
        var feesByTeamId = teamIds.Count > 0
            ? await _feeService.ResolveFeesByTeamIdsAsync(jobId, RoleConstants.Player, teamIds)
            : new Dictionary<Guid, ResolvedFee>();

        var bAddProcessingFees = job.BAddProcessingFees ?? false;
        var ccRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);
        var echeckRate = await _feeService.GetEffectiveEcheckProcessingRateAsync(jobId);
        var emptyState = PaymentState.Empty(bAddProcessingFees, ccRate, echeckRate);

        return rawPlayers.Select(p =>
        {
            var resolved = p.AssignedTeamId.HasValue
                ? feesByTeamId.GetValueOrDefault(p.AssignedTeamId.Value)
                : null;
            var deposit = resolved?.Deposit ?? 0m;
            var balanceDue = resolved?.BalanceDue ?? 0m;

            var state = paymentStates.GetValueOrDefault(p.RegistrationId, emptyState);
            // donation: 0m — a donation is always charged in full with its payment, so it sits in
            // BOTH the principal base and PrincipalPaid and nets out of these post-payment display
            // columns. RegisteredPlayerInfo doesn't carry FeeDonation; threading it would change
            // nothing here. (The charge/stamp paths pass the real FeeDonation.)
            // TotalDiscount() — both buckets, exactly what FeeMath subtracted from FeeTotal. Netting
            // only FeeDiscount here would overstate principal-remaining and drift these columns from
            // the stored OwedTotal.
            var discount = p.TotalDiscount();
            var depositDue = state.DepositPrincipalRemaining(deposit, discount, p.FeeLatefee, donation: 0m);

            // Per-player phase: deposit phase (FeeBase == Deposit) shows the structural balance
            // forward; once upgraded to pay-in-full (FeeBase covers deposit + balance) the
            // balance nets payments via the canonical helper. Detected per-row, not via a job
            // flag — family siblings can be on different phases.
            var bFull = p.FeeBase >= (resolved?.FullPrice ?? 0m) - 0.005m;
            var additionalDue = bFull
                ? state.BalancePrincipalRemaining(p.FeeBase, deposit, discount, p.FeeLatefee, donation: 0m)
                : balanceDue;

            var owed = state.ResolveOwed(p.OwedTotal, p.FeeBase, discount, p.FeeLatefee, donation: 0m, p.FeeProcessing);

            return new RegisteredTeamDto
            {
                TeamId = p.RegistrationId,        // doubles as the ledger group key (= record.OwnerRegistrationId)
                TeamName = p.PlayerName,
                AgeGroupId = p.AgeGroupId ?? Guid.Empty,
                AgeGroupName = "",                // age-group column hidden for the family grid
                LevelOfPlay = null,
                ClubTeamId = null,
                BHasBeenScheduled = false,
                FeeBase = p.FeeBase,
                FeeProcessing = p.FeeProcessing,                       // READ from record — the proc the family grid shows
                FeeProcessingDue = Math.Max(0m, owed.Cc - owed.Check), // canonical "proc still owed if CC-billed"
                FeeDiscount = p.FeeDiscount,
                FeeLatefee = p.FeeLatefee,
                FeeTotal = p.FeeTotal,
                PaidTotal = p.PaidTotal,
                OwedTotal = p.OwedTotal,
                FeeAdj = state.FeeAdjustment(discount, p.FeeLatefee),
                TenderPaid = state.TenderPaid,
                Deposit = deposit,
                BalanceDue = balanceDue,
                FullPaymentRequired = bFull,     // per-row stamped phase (FeeBase >= FullPrice)
                DepositDue = depositDue,
                AdditionalDue = additionalDue,
                RegistrationTs = p.RegistrationTs,
                BWaiverSigned3 = false,
                CcOwedTotal = owed.Cc,
                CkOwedTotal = owed.Check,
                EkOwedTotal = owed.Echeck,
                Active = p.Active,
                PaymentScheduled = false,
                NextChargeDate = null
            };
        }).ToList();
    }
}
