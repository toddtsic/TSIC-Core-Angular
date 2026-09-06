namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Usage report 01 -- Users by Role. Distinct people who used the scoped live events in
/// the window, counted by REGISTRATION, keyed by the event the request was about and
/// grouped by the registration's role.
///
/// Registration, not login, is the unit on purpose: role belongs to the registration.
/// A parent who is also a coach appears once under Family and once under Staff, which
/// is what "by role" means to the director reading it.
///
/// Rows are per event so the page can chart one cluster per live event (or one, when
/// an event lens is set). The event key is the job the request was ABOUT -- the same
/// rule the log itself uses -- so a Superuser working in event B's console counts
/// under B.
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

    /// <summary>Live events the numbers cover -- the resolved scope (after any event lens), restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per (event, role) that had at least one user. Unordered; the page sorts for display.</summary>
    public required List<UsersByRoleRowDto> Rows { get; init; }

    /// <summary>
    /// Distinct registrations across the customer-facing roles -- the director's "people
    /// using my event". Distinct, not a sum of rows: a registration that touched two
    /// events is one person.
    /// </summary>
    public required int CustomerUsers { get; init; }

    /// <summary>Distinct registrations across the admin tier: the director's own staff doing setup, plus TSIC.</summary>
    public required int AdminUsers { get; init; }

    /// <summary>
    /// One row per role across the WHOLE resolved scope: distinct registrations under that
    /// role with a request about ANY event in the set. This is what an "All events" chart
    /// shows. Distinct, not a sum of Rows: a family using two events is one person here,
    /// so a Totals figure can be smaller than the column sum in a per-event table.
    /// </summary>
    public required List<UsersByRoleTotalDto> Totals { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

/// <summary>A scope-wide role total for UsersByRoleDto.Totals.</summary>
public record UsersByRoleTotalDto
{
    public required string RoleName { get; init; }

    /// <summary>Distinct registrations under this role with at least one request about any event in the scope.</summary>
    public required int Users { get; init; }

    public required bool IsAdmin { get; init; }
}

public record UsersByRoleRowDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    public required string RoleName { get; init; }

    /// <summary>Distinct registrations under this role with at least one request about this event in the window.</summary>
    public required int Users { get; init; }

    /// <summary>
    /// True for the admin tier (RoleConstants.AdminRoleIds). Shown apart so staff doing
    /// setup never pads the customer-facing count.
    /// </summary>
    public required bool IsAdmin { get; init; }
}

/// <summary>A (event, registration) pair from TSICLogs: this registration made at least one request about this event.</summary>
public record UsageRegistrationByJobDto
{
    public required Guid JobId { get; init; }

    public required Guid RegistrationId { get; init; }
}

/// <summary>A registration's role, for mapping TSICLogs registration ids back to roles in TSICV5.</summary>
public record UsageRegistrationRoleDto
{
    public required Guid RegistrationId { get; init; }

    public required string RoleId { get; init; }

    public required string RoleName { get; init; }
}
