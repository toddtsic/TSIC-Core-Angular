using TSIC.API.DTOs;

namespace TSIC.API.Services;

public interface IRegistrationQueryService
{
    Task<object> GetExistingRegistrationAsync(string jobPath, string familyUserId, string callerId);
    Task<IEnumerable<FamilyRegistrationItemDto>> GetFamilyRegistrationsAsync(string jobPath, string familyUserId, string callerId);
}
