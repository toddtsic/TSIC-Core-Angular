using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record RegisteredTeamInfo(
    Guid TeamId,
    string TeamName,
    Guid AgeGroupId,
    string AgeGroupName,
    string? LevelOfPlay,
    decimal FeeBase,
    decimal FeeProcessing,
    decimal FeeTotal,
    decimal PaidTotal,
    decimal OwedTotal,
    decimal DepositDue,
    decimal AdditionalDue,
    DateTime RegistrationTs,
    bool BWaiverSigned3);

public record AvailableTeamQueryResult(
    Guid TeamId,
    string Name,
    Guid AgegroupId,
    string AgegroupName,
    Guid? DivisionId,
    string? DivisionName,
    int MaxCount,
    decimal? RawPerRegistrantFee,
    decimal? RawPerRegistrantDeposit,
    decimal? RawTeamFee,
    decimal? RawRosterFee,
    bool? TeamAllowsSelfRostering,
    bool? AgegroupAllowsSelfRostering,
    decimal? LeaguePlayerFeeOverride,
    decimal? AgegroupPlayerFeeOverride);

public record TeamFeeData(
    decimal? PerRegistrantFee,
    decimal? PerRegistrantDeposit,
    decimal? TeamFee,
    decimal? RosterFee,
    decimal? LeaguePlayerFeeOverride,
    decimal? AgegroupPlayerFeeOverride);

/// <summary>
/// Repository for managing Teams entity data access.
/// </summary>
public interface ITeamRepository
{
    /// <summary>
    /// Get a queryable for Teams queries
    /// </summary>
    IQueryable<Teams> Query();

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

