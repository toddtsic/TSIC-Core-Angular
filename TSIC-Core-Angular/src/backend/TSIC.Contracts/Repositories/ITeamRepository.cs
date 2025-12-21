using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record RegisteredTeamInfo(
    Guid TeamId,
    int ClubTeamId,
    string ClubTeamName,
    string ClubTeamGradYear,
    string ClubTeamLevelOfPlay,
    Guid AgeGroupId,
    string AgeGroupName,
    decimal FeeBase,
    decimal FeeProcessing,
    decimal FeeTotal,
    decimal PaidTotal,
    decimal OwedTotal);

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
    /// Get team by ID with Club, ClubTeam, and Agegroup navigation properties.
    /// </summary>
    Task<Teams?> GetTeamWithDetailsAsync(
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
}

