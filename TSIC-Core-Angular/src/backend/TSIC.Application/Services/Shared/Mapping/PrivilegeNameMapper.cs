namespace TSIC.Application.Services.Shared.Mapping;

/// <summary>
/// Pure business logic for mapping role IDs to human-readable privilege names.
/// Centralizes privilege naming rules to ensure consistent display across the application.
/// </summary>
public static class PrivilegeNameMapper
{
    /// <summary>
    /// Maps a role ID to its display name.
    /// </summary>
    /// <param name="roleId">The role ID from RoleConstants (e.g., RoleConstants.Player, RoleConstants.Staff).</param>
    /// <returns>The human-readable privilege name, or "Unknown" if the role ID is not recognized.</returns>
    public static string GetPrivilegeName(string? roleId)
    {
        return roleId switch
        {
            Domain.Constants.RoleConstants.Player => "Player",
            Domain.Constants.RoleConstants.Staff => "Staff",
            Domain.Constants.RoleConstants.ClubRep => "Club Rep",
            Domain.Constants.RoleConstants.Director => "Director",
            Domain.Constants.RoleConstants.SuperDirector => "Super Director",
            Domain.Constants.RoleConstants.Superuser => "Superuser",
            _ => "Unknown"
        };
    }
}

