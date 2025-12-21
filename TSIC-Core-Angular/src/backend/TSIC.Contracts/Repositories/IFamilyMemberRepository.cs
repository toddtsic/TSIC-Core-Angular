using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing FamilyMembers entity data access.
/// Encapsulates queries and write operations for linking family to child users.
/// </summary>
public interface IFamilyMemberRepository
{
    /// <summary>
    /// Get all linked child user IDs for a family.
    /// </summary>
    Task<List<string>> GetChildUserIdsAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new family member link (does NOT call SaveChanges).
    /// </summary>
    void Add(FamilyMembers familyMember);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
