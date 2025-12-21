using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IFamilyService
{
    Task<FamilyProfileResponse?> GetMyFamilyAsync(string userId);
    Task<FamilyRegistrationResponse> RegisterAsync(FamilyRegistrationRequest request);
    Task<FamilyRegistrationResponse> UpdateAsync(FamilyUpdateRequest request);
    Task<FamilyPlayersResponseDto> GetFamilyPlayersAsync(string familyUserId, string jobPath);
}
