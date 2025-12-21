using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Families entity data access.
/// Encapsulates all EF Core queries related to family records.
/// </summary>
public interface IFamilyRepository
{
    /// <summary>
    /// Get a family record by family user ID
    /// </summary>
    Task<Families?> GetByFamilyUserIdAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations for a family within a specific job with included user and team data
    /// </summary>
    Task<List<Registrations>> GetFamilyRegistrationsForJobAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get raw queryable for advanced filtering
    /// </summary>
    IQueryable<Families> Query();
}
