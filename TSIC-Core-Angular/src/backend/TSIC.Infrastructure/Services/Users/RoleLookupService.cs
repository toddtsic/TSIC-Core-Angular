using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Application.Services.Users;
using System.Collections.Generic;
using System.Threading.Tasks;
using TSIC.Domain.Constants;

namespace TSIC.Infrastructure.Services.Users;

/// <summary>
/// Service for looking up user role registrations.
/// Completely abstracted from data access layer - all queries encapsulated in repositories.
/// Focuses purely on business logic (filtering and privilege separation).
/// </summary>
public class RoleLookupService : IRoleLookupService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IUserPrivilegeLevelService _privilegeService;

    public RoleLookupService(
        IRegistrationRepository registrationRepo,
        IUserPrivilegeLevelService privilegeService)
    {
        _registrationRepo = registrationRepo;
        _privilegeService = privilegeService;
    }

    public async Task<List<RegistrationRoleDto>> GetRegistrationsForUserAsync(string userId)
    {
        var model = new List<RegistrationRoleDto>();

        // Query each role type through the repository
        // Each method encapsulates its complete multi-table join logic
        var lSuperUserRoles = await _registrationRepo.GetSuperUserRegistrationsAsync(userId);
        if (lSuperUserRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Superuser", lSuperUserRoles));
        }

        var lSuperDirectorRoles = await _registrationRepo.GetSuperDirectorRegistrationsAsync(userId);
        if (lSuperDirectorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("SuperDirector", lSuperDirectorRoles));
        }

        var lDirectorRoles = await _registrationRepo.GetDirectorRegistrationsAsync(userId);
        if (lDirectorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Director", lDirectorRoles));
        }

        var lFamilyRoles = await _registrationRepo.GetPlayerRegistrationsAsync(userId);
        if (lFamilyRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Player", lFamilyRoles));
        }

        var lClubRepRoles = await _registrationRepo.GetClubRepRegistrationsAsync(userId);
        if (lClubRepRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Club Rep", lClubRepRoles));
        }

        var lStaffRoles = await _registrationRepo.GetStaffRegistrationsAsync(userId);
        if (lStaffRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Staff", lStaffRoles));
        }

        var lStoreAdminRoles = await _registrationRepo.GetStoreAdminRegistrationsAsync(userId);
        if (lStoreAdminRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Store Admin", lStoreAdminRoles));
        }

        var lRefAssignorRoles = await _registrationRepo.GetRefAssignorRegistrationsAsync(userId);
        if (lRefAssignorRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Ref Assignor", lRefAssignorRoles));
        }

        var lRefRoles = await _registrationRepo.GetRefereeRegistrationsAsync(userId);
        if (lRefRoles.Count > 0)
        {
            model.Add(new RegistrationRoleDto("Referee", lRefRoles));
        }

        // Apply privilege separation filtering for historical violations
        model = FilterToLeastPrivilegedRole(model);

        return model;
    }

    /// <summary>
    /// Filter registrations to show only least privileged role if mixed privileges exist.
    /// This handles historical violations before the account separation policy was implemented.
    /// </summary>
    private List<RegistrationRoleDto> FilterToLeastPrivilegedRole(List<RegistrationRoleDto> registrations)
    {
        if (registrations.Count <= 1)
        {
            // Only one privilege level - no filtering needed
            return registrations;
        }

        // Extract all unique role IDs from registrations
        var roleIds = registrations.Select(r => GetRoleIdFromRoleName(r.RoleName)).Where(id => id != null).ToList();

        if (roleIds.Count <= 1)
        {
            // Only one privilege level - no filtering needed
            return registrations;
        }

        // Get least privileged role
        var leastPrivilegedRole = _privilegeService.GetLeastPrivilegedRole(roleIds!);
        var leastPrivilegedRoleName = GetRoleNameFromRoleId(leastPrivilegedRole);

        // Filter to show only the least privileged role
        return registrations.Where(r => r.RoleName == leastPrivilegedRoleName).ToList();
    }

    private static string? GetRoleIdFromRoleName(string roleName)
    {
        return roleName switch
        {
            "Family" => RoleConstants.Family,
            "Player" => RoleConstants.Player,
            "Staff" => RoleConstants.Staff,
            "Club Rep" => RoleConstants.ClubRep,
            "Director" => RoleConstants.Director,
            "SuperDirector" => RoleConstants.SuperDirector,
            "Superuser" => RoleConstants.Superuser,
            "Referee" => RoleConstants.Referee,
            "Ref Assignor" => RoleConstants.RefAssignor,
            _ => null
        };
    }

    private static string GetRoleNameFromRoleId(string roleId)
    {
        return roleId switch
        {
            RoleConstants.Family => "Family",
            RoleConstants.Player => "Player",
            RoleConstants.Staff => "Staff",
            RoleConstants.ClubRep => "Club Rep",
            RoleConstants.Director => "Director",
            RoleConstants.SuperDirector => "SuperDirector",
            RoleConstants.Superuser => "Superuser",
            RoleConstants.Referee => "Referee",
            RoleConstants.RefAssignor => "Ref Assignor",
            _ => "Unknown"
        };
    }
}



