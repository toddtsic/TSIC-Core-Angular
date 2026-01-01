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
    /// Get a queryable for Agegroups queries
    /// </summary>
    IQueryable<Agegroups> Query();
}
