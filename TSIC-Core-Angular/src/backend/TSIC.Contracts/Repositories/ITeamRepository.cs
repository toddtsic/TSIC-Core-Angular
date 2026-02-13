using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record RegisteredTeamInfo
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    public string? LevelOfPlay { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal DepositDue { get; init; }
    public required decimal AdditionalDue { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public required bool BWaiverSigned3 { get; init; }
    public int? ClubTeamId { get; init; }
}

public record AvailableTeamQueryResult
{
    public required Guid TeamId { get; init; }
    public required string Name { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public Guid? DivisionId { get; init; }
    public string? DivisionName { get; init; }
    public required int MaxCount { get; init; }
    public decimal? RawPerRegistrantFee { get; init; }
    public decimal? RawPerRegistrantDeposit { get; init; }
    public decimal? RawTeamFee { get; init; }
    public decimal? RawRosterFee { get; init; }
    public bool? TeamAllowsSelfRostering { get; init; }
    public bool? AgegroupAllowsSelfRostering { get; init; }
    public decimal? LeaguePlayerFeeOverride { get; init; }
    public decimal? AgegroupPlayerFeeOverride { get; init; }
}

public record TeamFeeData
{
    public decimal? PerRegistrantFee { get; init; }
    public decimal? PerRegistrantDeposit { get; init; }
    public decimal? TeamFee { get; init; }
    public decimal? RosterFee { get; init; }
    public decimal? LeaguePlayerFeeOverride { get; init; }
    public decimal? AgegroupPlayerFeeOverride { get; init; }
}

/// <summary>
/// Repository for managing Teams entity data access.
/// </summary>
public interface ITeamRepository
{
    /// <summary>
    /// Get teams by club and job, excluding specific registration.
    /// Used for checking conflicts when multiple club reps try to register teams.
    /// Joins to Registrations → ClubReps to verify club association.
    /// </summary>
    Task<List<TeamWithRegistrationInfo>> GetTeamsByClubExcludingRegistrationAsync(
        Guid jobId,
        int clubId,
        Guid? excludeRegistrationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get historical teams for team name suggestions.
    /// Returns teams from previous year's jobs for the same club.
    /// </summary>
    Task<List<HistoricalTeamInfo>> GetHistoricalTeamsForClubAsync(
        string userId,
        string clubName,
        int previousYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team registration counts grouped by age group for a job.
    /// Returns count of active teams per age group.
    /// </summary>
    Task<Dictionary<Guid, int>> GetRegistrationCountsByAgeGroupAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if any teams exist for a club rep (for preventing club removal).
    /// </summary>
    Task<bool> HasTeamsForClubRepAsync(
        string userId,
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team's job ID for authorization checks.
    /// </summary>
    Task<Guid?> GetTeamJobIdAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams with job and age group details for fee recalculation.
    /// </summary>
    Task<List<Teams>> GetTeamsWithDetailsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team's age group ID for fee calculations.
    /// </summary>
    Task<Guid?> GetTeamAgeGroupIdAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams for a job filtered by team IDs.
    /// </summary>
    Task<List<Teams>> GetTeamsForJobAsync(Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get fee-related information for a single team.
    /// </summary>
    Task<(decimal? FeeBase, decimal? PerRegistrantFee)> GetTeamFeeInfoAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registered teams for a club and job with full details.
    /// </summary>
    Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForClubAndJobAsync(
        Guid jobId,
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of registered teams for a specific agegroup and job.
    /// </summary>
    Task<int> GetRegisteredCountForAgegroupAsync(
        Guid jobId,
        Guid agegroupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team by ID using FindAsync (loads from identity map).
    /// </summary>
    Task<Teams?> GetTeamFromTeamId(
        Guid teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add new team (does NOT call SaveChanges).
    /// </summary>
    void Add(Teams team);

    /// <summary>
    /// Remove team (does NOT call SaveChanges).
    /// </summary>
    void Remove(Teams team);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available teams for a job (for self-rostering).
    /// </summary>
    Task<List<AvailableTeamQueryResult>> GetAvailableTeamsQueryResultsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team fee data for per-registrant fee calculation.
    /// </summary>
    Task<TeamFeeData?> GetTeamFeeDataAsync(
        Guid teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team names by team IDs for display purposes.
    /// </summary>
    Task<Dictionary<Guid, string>> GetTeamNameMapAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get teams for a job with Job and Customer navigation data (for payments/invoice numbers).
    /// </summary>
    Task<List<Teams>> GetTeamsWithJobAndCustomerAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registered teams for a club rep with payment-related details for insurance offer.
    /// Returns teams eligible for insurance purchase (active, has fees, not already insured).
    /// </summary>
    Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForPaymentAsync(
        Guid jobId,
        Guid clubRepRegId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registered teams for a user and job with full financial details.
    /// Filters out inactive teams and teams in age groups containing "DROPPED".
    /// Used by club rep for viewing registered teams and payment processing.
    /// </summary>
    Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForUserAndJobAsync(
        Guid jobId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk update team fees efficiently using UpdateRange.
    /// </summary>
    Task UpdateTeamFeesAsync(List<Teams> teams, CancellationToken cancellationToken = default);

    // ── LADT Admin methods ──

    /// <summary>
    /// Get all teams for a division (read-only, for LADT tree).
    /// </summary>
    Task<List<Teams>> GetByDivisionIdAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all teams for an agegroup (read-only, for LADT tree when no division).
    /// </summary>
    Task<List<Teams>> GetByAgegroupIdAsync(Guid agegroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team by ID with full details (read-only).
    /// </summary>
    Task<Teams?> GetByIdReadOnlyAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get max DivRank for a division (for new team ordering).
    /// </summary>
    Task<int> GetMaxDivRankAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the next available DivRank for a division (active team count + 1).
    /// Preferred over GetMaxDivRankAsync for new team creation to ensure contiguous ranking.
    /// </summary>
    Task<int> GetNextDivRankAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a team has any rostered players (for delete validation).
    /// </summary>
    Task<bool> HasRosteredPlayersAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get player count for a team.
    /// </summary>
    Task<int> GetPlayerCountAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get player counts for all teams in a job (bulk, for tree).
    /// </summary>
    Task<Dictionary<Guid, int>> GetPlayerCountsByTeamAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a team belongs to a job.
    /// </summary>
    Task<bool> BelongsToJobAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club names for all teams in a job (bulk, for tree/grid).
    /// Joins Teams → Registrations via ClubrepRegistrationid to get ClubName.
    /// Returns TeamId → ClubName mapping (only teams with a club rep).
    /// </summary>
    Task<Dictionary<Guid, string?>> GetClubNamesByJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club name for a single team (for detail view).
    /// Returns null if team has no ClubrepRegistrationid.
    /// </summary>
    Task<string?> GetClubNameForTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a team appears in any Schedule rows (T1Id or T2Id).
    /// </summary>
    Task<bool> IsTeamScheduledAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all team IDs that appear in any Schedule rows for a job (bulk, for tree).
    /// </summary>
    Task<HashSet<Guid>> GetScheduledTeamIdsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all teams in a job sharing the same ClubrepRegistrationid.
    /// Used for batch "move all teams from this club" operation.
    /// Returns tracked entities for in-place updates.
    /// </summary>
    Task<List<Teams>> GetTeamsByClubRepRegistrationAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default);

    // ── Pool Assignment methods ──

    /// <summary>
    /// Get teams for a division with club names, roster counts, and schedule status.
    /// </summary>
    Task<List<Dtos.PoolAssignment.PoolTeamDto>> GetPoolAssignmentTeamsAsync(
        Guid divId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get tracked team entities for transfer mutation.
    /// </summary>
    Task<List<Teams>> GetTeamsForPoolTransferAsync(
        List<Guid> teamIds, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Renumber DivRanks sequentially for active teams in a division (1, 2, 3, ...).
    /// </summary>
    Task RenumberDivRanksAsync(Guid divId, CancellationToken ct = default);

    /// <summary>
    /// Get the active team that currently holds a specific DivRank in a division.
    /// Returns tracked entity for swap mutation. Null if no team at that rank.
    /// </summary>
    Task<Teams?> GetTeamByDivRankAsync(Guid divId, int divRank, CancellationToken ct = default);

    // ── Roster Swapper methods ──

    /// <summary>
    /// Get all team pools for the Roster Swapper dropdown.
    /// Returns teams with agegroup/division names, roster counts, and a synthetic Unassigned Adults entry.
    /// </summary>
    Task<List<SwapperPoolOptionDto>> GetSwapperPoolOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get team with its parent agegroup for fee coalescing. AsNoTracking.
    /// </summary>
    Task<(Teams Team, Agegroups Agegroup)?> GetTeamWithFeeContextAsync(Guid teamId, CancellationToken ct = default);

    // ── Team Search methods ──

    /// <summary>
    /// Search teams by multiple filter criteria. AsNoTracking.
    /// Joins Teams → Agegroups → Divisions → ClubrepRegistration → AspNetUsers for all columns.
    /// </summary>
    Task<List<TeamSearchResultDto>> SearchTeamsAsync(Guid jobId, TeamSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get filter options with counts for the team search filter panel.
    /// Returns distinct clubs, LOPs, agegroups, active statuses, and pay statuses with counts.
    /// </summary>
    Task<TeamFilterOptionsDto> GetTeamSearchFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get team detail with club rep info for the detail panel. AsNoTracking.
    /// Joins Teams → Agegroups → Divisions → ClubrepRegistration → AspNetUsers.
    /// </summary>
    Task<TeamDetailQueryResult?> GetTeamDetailAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Get all active club teams for a club rep in a job, ordered by OwedTotal DESC.
    /// Returns tracked entities for cross-club payment mutation.
    /// </summary>
    Task<List<Teams>> GetActiveClubTeamsOrderedByOwedAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default);

    /// <summary>
    /// Get club team summaries for a club rep (for club-wide scope selector in detail panel). AsNoTracking.
    /// </summary>
    Task<List<ClubTeamSummaryDto>> GetClubTeamSummariesAsync(Guid jobId, Guid clubRepRegistrationId, CancellationToken ct = default);
}

public record TeamWithRegistrationInfo
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? Username { get; init; }
    public Guid? ClubrepRegistrationid { get; init; }
}

public record HistoricalTeamInfo
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? AgegroupName { get; init; }
    public required DateTime Createdate { get; init; }
}

/// <summary>
/// Flat query result for team detail panel.
/// Populated from Teams → Agegroups → Divisions → ClubrepRegistration → AspNetUsers join.
/// </summary>
public record TeamDetailQueryResult
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? ClubName { get; init; }
    public required string AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? LevelOfPlay { get; init; }
    public required bool Active { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? TeamComments { get; init; }
    public Guid? ClubRepRegistrationId { get; init; }
    public string? ClubRepName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }
    public Guid JobId { get; init; }
}
