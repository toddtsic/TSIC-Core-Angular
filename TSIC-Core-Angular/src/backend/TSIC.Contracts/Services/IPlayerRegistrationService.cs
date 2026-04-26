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
    /// Re-stamps FeeBase on every active player registration in a job per the
    /// job's current Jobs.BPlayersFullPaymentRequired phase. Mirror of
    /// ITeamRegistrationService.RecalculateTeamFeesAsync — invoked from
    /// JobConfigService.UpdatePaymentAsync when the flag changes.
    /// Returns the number of registrations updated.
    /// </summary>
    Task<int> RecalculatePlayerFeesAsync(Guid jobId, string userId, CancellationToken ct = default);
}
