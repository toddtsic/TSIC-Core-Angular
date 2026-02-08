using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Agegroups entity data access.
/// </summary>
public interface IAgeGroupRepository
{
    /// <summary>
    /// Get fee information (TeamFee and RosterFee) for an age group
    /// </summary>
    Task<(decimal? TeamFee, decimal? RosterFee)?> GetFeeInfoAsync(Guid ageGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get age groups by league ID and season, filtered by MaxTeams > 0.
    /// Returns age groups with their IDs, names, and MaxTeams for registration UI.
    /// </summary>
    Task<List<AgeGroupForRegistration>> GetByLeagueAndSeasonAsync(
        Guid leagueId,
        string season,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get age group by ID for validation.
    /// Returns age group info including MaxTeams for capacity checks.
    /// </summary>
    Task<AgeGroupValidationInfo?> GetForValidationAsync(
        Guid ageGroupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get age group by ID.
    /// </summary>
    Task<Agegroups?> GetByIdAsync(Guid ageGroupId, CancellationToken cancellationToken = default);

    // ── LADT Admin methods ──

    /// <summary>
    /// Get all agegroups for a league (read-only, for LADT tree).
    /// </summary>
    Task<List<Agegroups>> GetByLeagueIdAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if an agegroup has any teams (for delete validation).
    /// </summary>
    Task<bool> HasTeamsAsync(Guid agegroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if an agegroup belongs to a job (via League → JobLeagues).
    /// </summary>
    Task<bool> BelongsToJobAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default);

    void Add(Agegroups agegroup);
    void Remove(Agegroups agegroup);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public record AgeGroupForRegistration
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required int MaxTeams { get; init; }
    public decimal? TeamFee { get; init; }
    public decimal? RosterFee { get; init; }
}

public record AgeGroupValidationInfo
{
    public required Guid AgegroupId { get; init; }
    public string? AgegroupName { get; init; }
    public required int MaxTeams { get; init; }
    public decimal? TeamFee { get; init; }
    public decimal? RosterFee { get; init; }
}
