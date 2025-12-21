using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Shared.Registration;

public interface IRegistrationQueryService
{
    Task<object> GetExistingRegistrationAsync(string jobPath, string familyUserId, string callerId);
    Task<IEnumerable<FamilyRegistrationItemDto>> GetFamilyRegistrationsAsync(string jobPath, string familyUserId, string callerId);
}
