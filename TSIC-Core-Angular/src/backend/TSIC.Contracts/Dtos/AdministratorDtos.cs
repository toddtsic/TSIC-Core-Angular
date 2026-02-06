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
/// Request to batch-update active status for non-Superuser administrators.
/// </summary>
public record BatchUpdateStatusRequest
{
    public required bool IsActive { get; init; }
}

/// <summary>
/// Lightweight user result for typeahead search.
/// </summary>
public record UserSearchResultDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
}
