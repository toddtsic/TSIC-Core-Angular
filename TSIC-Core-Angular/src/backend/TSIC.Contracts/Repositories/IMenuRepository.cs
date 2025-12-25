using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing JobMenus and JobMenuItems entity data access.
/// </summary>
public interface IMenuRepository
{
    /// <summary>
    /// Get the best-fit menu for a job and role, with hierarchical structure.
    /// Precedence: Role-specific (active) → Anonymous (roleId NULL, active) → null
    /// </summary>
    /// <param name="jobId">The job ID to filter menus</param>
    /// <param name="roleName">The role name to filter menus (null for anonymous)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Menu DTO with hierarchical items, or null if no matching menu found</returns>
    Task<MenuDto?> GetMenuForJobAndRoleAsync(
        Guid jobId,
        string? roleName = null,
        CancellationToken cancellationToken = default);
}
