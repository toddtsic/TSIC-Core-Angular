using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing AspNetUsers entity data access.
/// Encapsulates all EF Core queries related to user accounts.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Get an AspNetUser by user ID
    /// </summary>
    Task<AspNetUsers?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a queryable for AspNetUser queries
    /// </summary>
    IQueryable<AspNetUsers> Query();
}
