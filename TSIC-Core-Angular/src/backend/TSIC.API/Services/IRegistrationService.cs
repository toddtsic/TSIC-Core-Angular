using TSIC.API.Dtos;

namespace TSIC.API.Services;

public interface IRegistrationService
{
    Task<PreSubmitRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitRegistrationRequestDto request, string callerUserId);
}
