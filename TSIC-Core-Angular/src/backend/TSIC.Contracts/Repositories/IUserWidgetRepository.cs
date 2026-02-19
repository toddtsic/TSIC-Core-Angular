using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for per-user widget customization (UserWidget table).
/// </summary>
public interface IUserWidgetRepository
{
    /// <summary>
    /// Get all user widget customizations for a registration.
    /// </summary>
    Task<List<UserWidget>> GetByRegistrationIdAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove all user widget customizations for a registration.
    /// </summary>
    void RemoveRange(IEnumerable<UserWidget> entities);

    /// <summary>
    /// Add a batch of user widget customizations.
    /// </summary>
    Task AddRangeAsync(
        IEnumerable<UserWidget> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
