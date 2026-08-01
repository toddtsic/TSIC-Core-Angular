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
    /// Team hydration is slice-aware WITHOUT caller involvement: on a proc-on-balance-only
    /// job the service resolves each team's effective deposit itself (fee cascade + modifier
    /// columns, repository-level) and decomposes CC/eCheck gross accordingly — see
    /// <see cref="PaymentState.ProcFreeBase"/>. All other configs (and all player
    /// hydration) behave exactly as pre-slice.
    /// </summary>
    Task<PaymentState> ForTeamAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default);

    Task<Dictionary<Guid, PaymentState>> ForRegistrationsAsync(
        IReadOnlyCollection<Guid> registrationIds, Guid jobId, CancellationToken ct = default);

    /// <summary>Batch form of <see cref="ForTeamAsync"/> — same automatic slice awareness.</summary>
    Task<Dictionary<Guid, PaymentState>> ForTeamsAsync(
        IReadOnlyCollection<Guid> teamIds, Guid jobId, CancellationToken ct = default);
}
