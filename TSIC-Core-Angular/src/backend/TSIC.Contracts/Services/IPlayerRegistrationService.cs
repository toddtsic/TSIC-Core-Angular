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
}
