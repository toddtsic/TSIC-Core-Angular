using TSIC.Contracts.Payments;

namespace TSIC.Contracts.Services;

/// <summary>
/// Single source of truth for "what's the current payment state of this entity?"
/// Every recalc, display, and per-payment consumer goes through here so the
/// interpretive math (CC reverse-out, eCheck principal/proc split, recalc target)
/// lives in one place.
///
/// Reads payment rows + job rates and hydrates a <see cref="PaymentState"/> whose
/// derived properties answer the canonical questions:
///   FeeProcessingTarget(...)  — invariant for FeeProcessing on the entity
///   PrincipalRemaining(...)   — what's still owed if remainder paid by check
///   ProcFeeDue(...)           — what proc would still be charged on a CC remainder
/// </summary>
public interface IPaymentStateService
{
    /// <summary>
    /// A payment state carrying only the job's rates + BAddProcessingFees (no prior-payment
    /// totals). Sufficient for <see cref="PaymentState.ResolveOwed"/>, which takes the owed/fee
    /// figures as arguments — used by display paths to surface per-method owed (e.g. the eCheck
    /// total) without loading a specific entity's payment history.
    /// </summary>
    Task<PaymentState> ForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<PaymentState> ForRegistrationAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="procFreeBase"/>: the team's proc-free bill slice (effective deposit
    /// — deposit − discount + lateFee + donation) on a job with BAddProcessingFees=true and
    /// BApplyProcessingFeesToTeamDeposit=false; 0 (default) everywhere else. When &gt; 0 the
    /// hydration walks ledger rows oldest-first to split CC/eCheck gross into the proc-free
    /// portion (principal at face value) and the grossed-up remainder — see
    /// <see cref="PaymentState.ProcFreeBase"/>. Team-only: the flag does not exist for
    /// player registrations.
    /// </summary>
    Task<PaymentState> ForTeamAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default, decimal procFreeBase = 0m);

    Task<Dictionary<Guid, PaymentState>> ForRegistrationsAsync(
        IReadOnlyCollection<Guid> registrationIds, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="procFreeBaseByTeam"/>: per-team proc-free bill slice; see
    /// <see cref="ForTeamAsync"/>. Teams absent from the map (or null map) hydrate with no
    /// slice awareness — identical to the pre-slice behavior.
    /// </summary>
    Task<Dictionary<Guid, PaymentState>> ForTeamsAsync(
        IReadOnlyCollection<Guid> teamIds, Guid jobId, CancellationToken ct = default,
        IReadOnlyDictionary<Guid, decimal>? procFreeBaseByTeam = null);
}
