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
