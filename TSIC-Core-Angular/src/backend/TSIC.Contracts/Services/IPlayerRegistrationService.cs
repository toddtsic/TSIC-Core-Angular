using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IPlayerRegistrationService
{
    Task<PreSubmitPlayerRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request, string callerUserId);

    /// <summary>
    /// Phase 1 — reserve team spots at team selection time.
    /// Checks roster capacity, creates/updates pending registrations (BActive=false),
    /// and calculates fees. Does NOT apply form values or validate fields.
    /// </summary>
    Task<ReserveTeamsResponseDto> ReserveTeamsAsync(Guid jobId, string familyUserId, ReserveTeamsRequestDto request, string callerUserId);

    /// <summary>
    /// Re-stamps FeeBase on active player registrations per the effective full-payment
    /// phase, resolved per-scope from JobFees (team → agegroup → league) and falling back
    /// to Jobs.BPlayersFullPaymentRequired. The single canonical scoped player reprice:
    /// used by JobConfigService.UpdatePaymentAsync (job-wide), the per-scope phase toggle,
    /// and the LADT "Push Fees to Players" button. Optionally narrows to one agegroup
    /// and/or team; null scope = whole job. Paid-in-full rows are skipped.
    /// Returns the number of registrations updated.
    /// </summary>
    Task<int> RecalculatePlayerFeesAsync(
        Guid jobId, string userId, Guid? agegroupId = null, Guid? teamId = null,
        CancellationToken ct = default);

    /// <summary>
    /// The "blast area" for a player fee/phase change: how many active player registrations
    /// fall in scope. Mirrors <see cref="RecalculatePlayerFeesAsync"/>'s selection exactly so
    /// the count equals what a reprice would touch. <paramref name="teamId"/> wins; else
    /// <paramref name="agegroupIds"/> (one agegroup, or a whole league's agegroups); else the
    /// whole job. Read-only — informs the admin before a money change; no writes.
    /// </summary>
    Task<int> CountActivePlayersInScopeAsync(
        Guid jobId, IReadOnlyCollection<Guid>? agegroupIds, Guid? teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Pay-by-check intake: stamps PaymentMethodChosen=3 (Check) and BActive=true
    /// on each registration in <paramref name="request"/>.RegistrationIds so the
    /// roster spot is held while the check is in transit. No fee math performed.
    /// Idempotent. Strictly check-path: rejects rows already committed to a
    /// non-check method.
    /// </summary>
    Task<SubmitByCheckResponseDto> SubmitByCheckAsync(
        Guid jobId,
        string familyUserId,
        SubmitByCheckRequestDto request,
        string callerUserId,
        CancellationToken ct = default);
}
