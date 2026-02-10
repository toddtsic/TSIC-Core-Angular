namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Search request sent from the grid filter panel.
/// POST body because filter criteria can be complex.
/// </summary>
public record RegistrationSearchRequest
{
    // Text filters
    public string? Name { get; init; }
    public string? Email { get; init; }

    // Dropdown filters
    public string? RoleId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? AgegroupId { get; init; }
    public Guid? DivisionId { get; init; }
    public string? ClubName { get; init; }

    // Status filters
    public bool? Active { get; init; }
    public string? OwesFilter { get; init; }

    // Date range
    public DateTime? RegDateFrom { get; init; }
    public DateTime? RegDateTo { get; init; }

    // Paging & sorting
    public int Skip { get; init; }
    public int Take { get; init; } = 20;
    public string? SortField { get; init; }
    public string? SortDirection { get; init; }
}

/// <summary>
/// Single row in the registration search results grid.
/// </summary>
public record RegistrationSearchResultDto
{
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }

    // Person (from AspNetUsers join)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public DateTime? Dob { get; init; }

    // Registration context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? Position { get; init; }
    public string? TeamName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivisionName { get; init; }
    public string? ClubName { get; init; }

    // Financials
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dates
    public required DateTime RegistrationTs { get; init; }
    public DateTime? Modified { get; init; }
}

/// <summary>
/// Paged response wrapper with server-side aggregates.
/// </summary>
public record RegistrationSearchResponse
{
    public required List<RegistrationSearchResultDto> Result { get; init; }
    public required int Count { get; init; }

    // Aggregates across ALL matching records (not just current page)
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}

/// <summary>
/// Filter dropdown options loaded once on component init.
/// </summary>
public record RegistrationFilterOptionsDto
{
    public required List<FilterOption> Roles { get; init; }
    public required List<FilterOption> Teams { get; init; }
    public required List<FilterOption> Agegroups { get; init; }
    public required List<FilterOption> Divisions { get; init; }
    public required List<string> Clubs { get; init; }
}

public record FilterOption
{
    public required string Value { get; init; }
    public required string Text { get; init; }
}
