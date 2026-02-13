using TSIC.Contracts.Dtos.RegistrationSearch;

namespace TSIC.Contracts.Dtos.TeamSearch;

/// <summary>
/// Search request for the Team Search admin grid.
/// POST body because filter criteria can be complex.
/// </summary>
public record TeamSearchRequest
{
    // Multi-select filters
    public List<string>? ClubNames { get; init; }
    public List<string>? LevelOfPlays { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<string>? ActiveStatuses { get; init; }
    public List<string>? PayStatuses { get; init; }

    // LADT tree filter IDs (derived from tree checkbox selection)
    public List<Guid>? LeagueIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
}

/// <summary>
/// Single row in the team search results grid.
/// </summary>
public record TeamSearchResultDto
{
    public required Guid TeamId { get; init; }
    public required bool Active { get; init; }
    public string? ClubName { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? LevelOfPlay { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required DateTime RegDate { get; init; }
    public string? ClubRepName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }
    public string? TeamComments { get; init; }
}

/// <summary>
/// Response wrapper with aggregates across all matching teams.
/// </summary>
public record TeamSearchResponse
{
    public required List<TeamSearchResultDto> Result { get; init; }
    public required int Count { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}

/// <summary>
/// Filter options with counts, loaded once on component init.
/// Reuses FilterOption from RegistrationSearch.
/// </summary>
public record TeamFilterOptionsDto
{
    public required List<FilterOption> Clubs { get; init; }
    public required List<FilterOption> LevelOfPlays { get; init; }
    public required List<FilterOption> AgeGroups { get; init; }
    public required List<FilterOption> ActiveStatuses { get; init; }
    public required List<FilterOption> PayStatuses { get; init; }
}
