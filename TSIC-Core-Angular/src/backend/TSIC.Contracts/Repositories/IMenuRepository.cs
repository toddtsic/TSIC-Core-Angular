using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

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
    Task<MenuDto?> GetMenuForJobAndRoleAsync(
        Guid jobId,
        string? roleName = null,
        CancellationToken cancellationToken = default);

    // ─── Admin methods ────────────────────────────────────────────────

    /// <summary>
    /// Get all menus for a job, including inactive ones. Includes Role navigation for display.
    /// </summary>
    Task<List<JobMenus>> GetAllMenusForJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Get a menu by ID (tracked for updates).</summary>
    Task<JobMenus?> GetMenuByIdAsync(Guid menuId, CancellationToken cancellationToken = default);

    /// <summary>Get a menu item by ID (tracked for updates).</summary>
    Task<JobMenuItems?> GetMenuItemByIdAsync(Guid menuItemId, CancellationToken cancellationToken = default);

    /// <summary>Get all menu items for a menu (AsNoTracking, includes inactive).</summary>
    Task<List<JobMenuItems>> GetMenuItemsByMenuIdAsync(Guid menuId, CancellationToken cancellationToken = default);

    /// <summary>Get sibling items at the same level (tracked for reorder).</summary>
    Task<List<JobMenuItems>> GetSiblingItemsAsync(Guid menuId, Guid? parentMenuItemId, CancellationToken cancellationToken = default);

    /// <summary>Get count of sibling items at the same level.</summary>
    Task<int> GetSiblingCountAsync(Guid menuId, Guid? parentMenuItemId, CancellationToken cancellationToken = default);

    /// <summary>Get role IDs that already have menus for a given job.</summary>
    Task<List<string>> GetExistingMenuRoleIdsForJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Add a new menu entity to the context.</summary>
    void AddMenu(JobMenus menu);

    /// <summary>Add a new menu item entity to the context.</summary>
    void AddMenuItem(JobMenuItems menuItem);

    /// <summary>Remove a menu item entity from the context.</summary>
    void RemoveMenuItem(JobMenuItems menuItem);

    /// <summary>Save all pending changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
