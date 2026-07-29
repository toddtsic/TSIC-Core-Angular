namespace TSIC.Contracts.Dtos;

/// <summary>
/// Administrator registration summary for the admin management grid.
/// </summary>
public record AdministratorDto
{
    public required Guid RegistrationId { get; init; }
    public required string AdministratorName { get; init; }
    public required string UserName { get; init; }
    public string? RoleName { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime RegisteredDate { get; init; }
    public required bool IsSuperuser { get; init; }
    public required bool IsPrimaryContact { get; init; }
}

/// <summary>
/// Request to add a new administrator registration to a job.
/// </summary>
public record AddAdministratorRequest
{
    public required string UserName { get; init; }
    public required string RoleName { get; init; }
}

/// <summary>
/// Request to update an existing administrator registration.
/// </summary>
public record UpdateAdministratorRequest
{
    public required bool IsActive { get; init; }
    public required string RoleName { get; init; }
}

/// <summary>
/// Typeahead search response. When <see cref="Results"/> is empty, <see cref="EmptyReason"/>
/// tells the UI which funnel message to show (AM-004): "notFound" — no account with a footprint
/// on this customer matches; "familyOrPlayer" — a match exists but is a family/player credential
/// (structurally barred from admin roles); "outsideLane" — a match exists but its registration
/// history lies outside the granted role's lane.
/// </summary>
public record UserSearchResponseDto
{
    public required List<UserSearchResultDto> Results { get; init; }

    /// <summary>"notFound" | "familyOrPlayer" | "outsideLane"; null when Results is non-empty.</summary>
    public string? EmptyReason { get; init; }
}

/// <summary>
/// Lightweight user result for typeahead search.
/// </summary>
public record UserSearchResultDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// "Admin" — established admin account (all registrations admin-role);
    /// "PendingAdult" — Unassigned Adult awaiting elevation (accepting converts
    /// their pending registration to the chosen admin role). AM-004.
    /// </summary>
    public required string AccountType { get; init; }
}
