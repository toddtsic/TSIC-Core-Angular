using TSIC.Application.DTOs;

namespace TSIC.Application.Services
{
    public interface IRoleLookupService
    {
        // Example method signature for role lookup
        Task<List<RegistrationRoleDto>> GetRegistrationsForUserAsync(string userId);
    }
}
