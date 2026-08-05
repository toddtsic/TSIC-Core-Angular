using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Application.Services.Users;

namespace TSIC.Infrastructure.Services.Users;

/// <summary>
/// Returns every active registration the user owns, grouped by role.
/// This is a directory read — privilege-separation policy is enforced at
/// registration creation time (see IUserPrivilegeLevelService), not here.
/// Filtering at this layer would silently mask legitimate multi-tier admin
/// accounts (e.g. Director + SuperDirector for the same person).
/// </summary>
public class RoleLookupService : IRoleLookupService
{
    private readonly IRegistrationRepository _registrationRepo;

    public RoleLookupService(IRegistrationRepository registrationRepo)
    {
        _registrationRepo = registrationRepo;
    }

    public async Task<List<RegistrationRoleDto>> GetRegistrationsForUserAsync(string userId)
    {
        var model = new List<RegistrationRoleDto>();

        var lSuperUserRoles = await _registrationRepo.GetSuperUserRegistrationsAsync(userId);
        if (lSuperUserRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Superuser", RoleRegistrations = lSuperUserRoles });
        }

        var lSuperDirectorRoles = await _registrationRepo.GetSuperDirectorRegistrationsAsync(userId);
        if (lSuperDirectorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "SuperDirector", RoleRegistrations = lSuperDirectorRoles });
        }

        var lDirectorRoles = await _registrationRepo.GetDirectorRegistrationsAsync(userId);
        if (lDirectorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Director", RoleRegistrations = lDirectorRoles });
        }

        var lFamilyRoles = await _registrationRepo.GetPlayerRegistrationsAsync(userId);
        if (lFamilyRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Player", RoleRegistrations = lFamilyRoles });
        }

        var lClubRepRoles = await _registrationRepo.GetClubRepRegistrationsAsync(userId);
        if (lClubRepRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Club Rep", RoleRegistrations = lClubRepRoles });
        }

        var lStaffRoles = await _registrationRepo.GetStaffRegistrationsAsync(userId);
        if (lStaffRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Staff", RoleRegistrations = lStaffRoles });
        }

        var lStoreAdminRoles = await _registrationRepo.GetStoreAdminRegistrationsAsync(userId);
        if (lStoreAdminRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Store Admin", RoleRegistrations = lStoreAdminRoles });
        }

        var lRefAssignorRoles = await _registrationRepo.GetRefAssignorRegistrationsAsync(userId);
        if (lRefAssignorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Ref Assignor", RoleRegistrations = lRefAssignorRoles });
        }

        var lRefRoles = await _registrationRepo.GetRefereeRegistrationsAsync(userId);
        if (lRefRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto { RoleName = "Referee", RoleRegistrations = lRefRoles });
        }

        return model;
    }
}
