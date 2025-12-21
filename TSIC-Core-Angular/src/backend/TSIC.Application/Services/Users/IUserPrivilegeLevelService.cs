using TSIC.Domain.Constants;

namespace TSIC.Application.Services.Users;

/// <summary>
/// Service for managing account privilege separation policy.
/// Ensures one account is locked to one privilege level to protect minor PII.
/// </summary>
public interface IUserPrivilegeLevelService
{
    /// <summary>
    /// Gets the privilege level that the user is locked to based on existing registrations.
    /// Returns null if the user has never registered for anything.
    /// </summary>
    /// <param name="userId">AspNetUser ID</param>
    /// <returns>The RoleId constant (e.g., RoleConstants.Player) or null if no registrations exist</returns>
    Task<string?> GetUserPrivilegeLevelAsync(string userId);

    /// <summary>
    /// Validates that the user can register for the target privilege level.
    /// Returns true if allowed (no prior registrations or matching privilege).
    /// Returns false if blocked (existing registrations at different privilege level).
    /// </summary>
    /// <param name="userId">AspNetUser ID</param>
    /// <param name="targetRoleId">Target privilege level (e.g., RoleConstants.ClubRep)</param>
    /// <returns>True if validation passes, false if privilege mismatch detected</returns>
    Task<bool> ValidatePrivilegeForRegistrationAsync(string userId, string targetRoleId);

    /// <summary>
    /// Gets the least privileged role for a user with mixed privileges (historical violations).
    /// Used for login list filtering to show only the lowest privilege.
    /// </summary>
    /// <param name="userId">AspNetUser ID</param>
    /// <param name="availableRoles">List of role IDs the user has registrations for</param>
    /// <returns>The RoleId constant representing the least privileged role</returns>
    string GetLeastPrivilegedRole(IEnumerable<string> availableRoles);
}

