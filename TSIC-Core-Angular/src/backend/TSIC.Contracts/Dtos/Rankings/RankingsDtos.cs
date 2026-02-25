namespace TSIC.Contracts.Dtos.Rankings;

/// <summary>
/// A single scraped ranking row from usclublax.com
/// </summary>
public record RankingEntryDto
{
    public required int Rank { get; init; }
    public required string Team { get; init; }
    public required string State { get; init; }
    public required string Record { get; init; }
    public required decimal Rating { get; init; }
    public required decimal Agd { get; init; }
    public required decimal Sched { get; init; }
}

/// <summary>
/// Result of a scrape operation against usclublax.com
/// </summary>
public record ScrapeResultDto
{
    public required bool Success { get; init; }
    public required string AgeGroup { get; init; }
    public required DateTime LastUpdated { get; init; }
    public string? ErrorMessage { get; init; }
    public required List<RankingEntryDto> Rankings { get; init; }
}

/// <summary>
/// Age group option for dropdowns (both scraped and registered)
/// </summary>
public record AgeGroupOptionDto
{
    public required string Value { get; init; }
    public required string Text { get; init; }
}

/// <summary>
/// Registered team info projected for the matching algorithm
/// </summary>
public record RankingsTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? AgeGroup { get; init; }
    public string? ClubName { get; init; }
    public string? Color { get; init; }
    public string? AgegroupName { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
    public string? TeamComments { get; init; }
}

/// <summary>
/// A matched ranking + registered team pair with confidence score
/// </summary>
public record AlignedTeamDto
{
    public required RankingEntryDto Ranking { get; init; }
    public required RankingsTeamDto RegisteredTeam { get; init; }
    public required double MatchScore { get; init; }
    public required string MatchReason { get; init; }
}

/// <summary>
/// Full alignment response with all matched/unmatched data
/// </summary>
public record AlignmentResultDto
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required string AgeGroup { get; init; }
    public required DateTime LastUpdated { get; init; }
    public required List<AlignedTeamDto> AlignedTeams { get; init; }
    public required List<RankingEntryDto> UnmatchedRankings { get; init; }
    public required List<RankingsTeamDto> UnmatchedTeams { get; init; }
    public required int TotalMatches { get; init; }
    public required int TotalTeamsInAgeGroup { get; init; }
    public required double MatchPercentage { get; init; }
}

/// <summary>
/// Request to bulk-import ranking data into TeamComments
/// </summary>
public record ImportCommentsRequest
{
    public required Guid RegisteredTeamAgeGroupId { get; init; }
    public required string ConfidenceCategory { get; init; }
    public required string V { get; init; }
    public required string Alpha { get; init; }
    public required string Yr { get; init; }
    public int ClubWeight { get; init; } = 75;
    public int TeamWeight { get; init; } = 25;
}

/// <summary>
/// Result of a bulk import operation
/// </summary>
public record ImportCommentsResultDto
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public required int UpdatedCount { get; init; }
    public required int TotalMatches { get; init; }
    public required string ConfidenceCategory { get; init; }
}

/// <summary>
/// Request to update a single team's comment
/// </summary>
public record UpdateTeamCommentRequest
{
    public required string Comment { get; init; }
}
