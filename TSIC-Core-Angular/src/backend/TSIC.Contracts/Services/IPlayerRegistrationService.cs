using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IPlayerRegistrationService
{
    Task<PreSubmitPlayerRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request, string callerUserId);
}
