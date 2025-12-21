using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Agegroups entity data access.
/// </summary>
public interface IAgeGroupRepository
{
    /// <summary>
    /// Get a queryable for Agegroups queries
    /// </summary>
    IQueryable<Agegroups> Query();
}
