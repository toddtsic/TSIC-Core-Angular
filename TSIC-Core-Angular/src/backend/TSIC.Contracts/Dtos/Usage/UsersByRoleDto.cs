namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Usage report 01 -- Users by Role. Distinct people who used the scoped live events in
/// the window, counted by REGISTRATION and grouped by the registration's role.
///
/// Registration, not login, is the unit on purpose: role belongs to the registration.
/// A parent who is also a coach appears once under Family and once under Staff, which
/// is what "by role" means to the director reading it.
///
/// Anonymous traffic is deliberately absent. Nothing in the fact table can turn an
/// anonymous request into a person (no session, device or address key), and a
/// registered user browsing before they sign in is anonymous too. Requests and people
/// are different units; this report carries only people.
/// </summary>
public record UsersByRoleDto
{
    public required int WindowDays { get; init; }

    public required bool BotsExcluded { get; init; }

    /// <summary>Live events the numbers cover -- the resolved scope, restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per role that had at least one user, admin tier last, then by users descending.</summary>
    public required List<UsersByRoleRowDto> Rows { get; init; }

    /// <summary>Users across the customer-facing roles -- the director's "people using my event".</summary>
    public required int CustomerUsers { get; init; }

    /// <summary>Users across the admin tier: the director's own staff doing setup, plus TSIC.</summary>
    public required int AdminUsers { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

public record UsersByRoleRowDto
{
    public required string RoleName { get; init; }

    /// <summary>Distinct registrations under this role with at least one request in the window.</summary>
    public required int Users { get; init; }

    /// <summary>
    /// True for the admin tier (RoleConstants.AdminRoleIds). Shown apart so staff doing
    /// setup never pads the customer-facing count.
    /// </summary>
    public required bool IsAdmin { get; init; }
}

/// <summary>A registration's role, for mapping TSICLogs registration ids back to roles in TSICV5.</summary>
public record UsageRegistrationRoleDto
{
    public required Guid RegistrationId { get; init; }

    public required string RoleId { get; init; }

    public required string RoleName { get; init; }
}
