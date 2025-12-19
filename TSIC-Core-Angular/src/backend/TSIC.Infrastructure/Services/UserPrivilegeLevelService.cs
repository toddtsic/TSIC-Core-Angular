using Microsoft.EntityFrameworkCore;
using TSIC.Application.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Services;

public sealed class UserPrivilegeLevelService : IUserPrivilegeLevelService
{
    private readonly SqlDbContext _db;

    // Privilege hierarchy: lower index = lower privilege
    private static readonly Dictionary<string, int> PrivilegeHierarchy = new()
    {
        { RoleConstants.Player, 1 },
        { RoleConstants.Staff, 2 },
        { RoleConstants.ClubRep, 3 },
        { RoleConstants.Director, 4 },
        { RoleConstants.SuperDirector, 5 },
        { RoleConstants.Superuser, 6 }
    };

    public UserPrivilegeLevelService(SqlDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetUserPrivilegeLevelAsync(string userId)
    {
        // Query all registrations for this user to find their locked privilege level
        var firstRegistration = await _db.Registrations
            .Where(r => r.UserId == userId && r.RoleId != null)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => r.RoleId)
            .FirstOrDefaultAsync();

        return firstRegistration;
    }

    public async Task<bool> ValidatePrivilegeForRegistrationAsync(string userId, string targetRoleId)
    {
        var existingPrivilege = await GetUserPrivilegeLevelAsync(userId);

        // No prior registrations = allowed
        if (existingPrivilege == null)
        {
            return true;
        }

        // Same privilege level = allowed
        if (existingPrivilege == targetRoleId)
        {
            return true;
        }

        // Different privilege level = blocked
        return false;
    }

    public string GetLeastPrivilegedRole(IEnumerable<string> availableRoles)
    {
        if (!availableRoles.Any())
        {
            throw new ArgumentException("No roles provided", nameof(availableRoles));
        }

        // Filter to only known privilege roles
        var knownRoles = availableRoles.Where(r => PrivilegeHierarchy.ContainsKey(r)).ToList();

        if (!knownRoles.Any())
        {
            // If no known privilege roles, return first available (fallback for special roles)
            return availableRoles.First();
        }

        // Return role with lowest hierarchy value
        return knownRoles.MinBy(r => PrivilegeHierarchy[r])!;
    }
}
