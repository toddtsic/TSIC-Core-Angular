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
/// A season published by usclublax.com, e.g. Value "2025", Text "2025-26".
/// The site serves one season per page; Value is the yr query parameter.
/// </summary>
public record RankingSeasonDto
{
    public required string Value { get; init; }
    public required string Text { get; init; }
    public required bool IsCurrent { get; init; }
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
    public string? NationalRankingData { get; init; }
}

/// <summary>
/// JSON-serializable national ranking data stored on Teams.NationalRankingData.
/// Contains the full scraped row from usclublax.com plus match metadata.
/// </summary>
public record NationalRankingDataDto
{
    public required int Rank { get; init; }
    public required string Team { get; init; }
    public required string State { get; init; }
    public required string Record { get; init; }
    public required decimal Rating { get; init; }
    public required decimal Agd { get; init; }
    public required decimal Sched { get; init; }
    public required double MatchScore { get; init; }
    public required DateTime MatchedAt { get; init; }

    /// <summary>
    /// The SEASON this rank came from -- the usclublax `yr`, e.g. "2025" for the 2025-26 season.
    /// Without it a rank stamped last season and one stamped this season are indistinguishable on
    /// the team row, and Pool Assignment sorts them side by side as if they were comparable.
    /// Nullable: blobs written before this field existed carry no season, and a blank reads
    /// honestly as "unknown" rather than being back-filled with a season we cannot verify.
    /// </summary>
    public string? Season { get; init; }

    /// <summary>
    /// The ranking family the row came from -- the usclublax `v`, e.g. "2030" = girls overall
    /// class of 2030, "2130" = girls national. Same nullability reasoning as <see cref="Season"/>.
    /// </summary>
    public string? RankingSource { get; init; }
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
/// One team's disposition in a save. The distinction between "omitted" and "present with a null
/// Ranking" is the whole contract, and it is deliberate:
///
///   omitted from the list  -> leave this team's stamp exactly as it is (below the chosen
///                             confidence threshold, or simply not part of this save)
///   present, Ranking null  -> CLEAR this team's stamp (the director un-matched it)
///   present, Ranking set   -> write this stamp
///
/// Absence must never mean "clear", or saving at "75%+" would silently wipe every medium-confidence
/// stamp the director deliberately kept.
/// </summary>
public record SaveRankingEntry
{
    public required Guid TeamId { get; init; }
    public NationalRankingDataDto? Ranking { get; init; }
}

/// <summary>
/// Save the rankings the director actually reviewed on screen.
///
/// This carries the decisions themselves, NOT the parameters to go re-derive them. The previous
/// contract sent the scrape parameters and had the server re-fetch usclublax.com and re-run the
/// whole fuzzy match at save time -- so an un-match never stuck (the server just re-matched it),
/// a hand correction raced the server's own answer, and the save failed outright whenever the
/// third-party site was slow or had changed. What the director sees is now what gets stored.
/// </summary>
public record SaveRankingsRequest
{
    /// <summary>Scopes the save: every TeamId must belong to this job AND this age group.</summary>
    public required Guid RegisteredTeamAgeGroupId { get; init; }
    public required List<SaveRankingEntry> Teams { get; init; }
}

/// <summary>
/// Result of a save. Writes and clears are reported separately -- "saved 12" when the director
/// also un-matched 3 hides half of what happened.
/// </summary>
public record SaveRankingsResultDto
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public required int UpdatedCount { get; init; }
    public required int ClearedCount { get; init; }
}
