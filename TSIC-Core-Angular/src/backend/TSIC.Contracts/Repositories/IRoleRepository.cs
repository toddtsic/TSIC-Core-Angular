using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing AspNetRoles entity data access.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Get a queryable for AspNetRole queries
    /// </summary>
    IQueryable<AspNetRoles> Query();
}
