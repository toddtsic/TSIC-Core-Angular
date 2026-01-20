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
}

public record AgeGroupForRegistration(
    Guid AgegroupId,
    string AgegroupName,
    int MaxTeams,
    decimal? TeamFee,
    decimal? RosterFee);

public record AgeGroupValidationInfo(
    Guid AgegroupId,
    string? AgegroupName,
    int MaxTeams,
    decimal? TeamFee,
    decimal? RosterFee);
