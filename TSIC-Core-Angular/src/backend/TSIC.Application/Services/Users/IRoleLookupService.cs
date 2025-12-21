using TSIC.Contracts.Dtos;

namespace TSIC.Application.Services.Users;

public interface IRoleLookupService
{
    // Example method signature for role lookup
    Task<List<RegistrationRoleDto>> GetRegistrationsForUserAsync(string userId);
}

