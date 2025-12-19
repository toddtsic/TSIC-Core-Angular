using TSIC.API.Dtos;

namespace TSIC.Application.Services;

public interface IPlayerRegistrationService
{
    Task<PreSubmitPlayerRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request, string callerUserId);
}
