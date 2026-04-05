using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IFamilyService
{
    Task<FamilyProfileResponse?> GetMyFamilyAsync(string userId);
    Task<ValidateCredentialsResponse> ValidateCredentialsAsync(ValidateCredentialsRequest request);
    Task<FamilyRegistrationResponse> RegisterAsync(FamilyRegistrationRequest request);
    Task<FamilyRegistrationResponse> UpdateAsync(FamilyUpdateRequest request);
    Task<FamilyPlayersResponseDto> GetFamilyPlayersAsync(string familyUserId, string jobPath);
    Task<ChildOperationResponse> AddChildAsync(string familyUserId, ChildDto request);
    Task<ChildOperationResponse> UpdateChildAsync(string familyUserId, string childUserId, ChildDto request);
}
