using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for SuperUser nav editor — CRUD for platform defaults and
/// job overrides, plus legacy menu access for import.
/// </summary>
public interface INavEditorRepository
{
    // ─── Platform defaults ──────────────────────────────────────────

    /// <summary>Get all platform default navs with items and role names.</summary>
    Task<List<NavEditorNavDto>> GetAllPlatformDefaultsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Get a single nav by ID (tracked for updates).</summary>
    Task<Nav?> GetNavByIdAsync(int navId, CancellationToken cancellationToken = default);

    /// <summary>Check if a platform default already exists for a role.</summary>
    Task<bool> PlatformDefaultExistsAsync(
        string roleId,
        CancellationToken cancellationToken = default);

    // ─── Job overrides ──────────────────────────────────────────────

    /// <summary>Get all job overrides for a specific job.</summary>
    Task<List<NavEditorNavDto>> GetJobOverridesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    // ─── Nav items ──────────────────────────────────────────────────

    /// <summary>Get a nav item by ID (tracked for updates).</summary>
    Task<NavItem?> GetNavItemByIdAsync(int navItemId, CancellationToken cancellationToken = default);

    /// <summary>Get all items for a nav (AsNoTracking).</summary>
    Task<List<NavItem>> GetNavItemsByNavIdAsync(int navId, CancellationToken cancellationToken = default);

    /// <summary>Get sibling items at the same level (tracked for reorder).</summary>
    Task<List<NavItem>> GetSiblingItemsAsync(
        int navId,
        int? parentNavItemId,
        CancellationToken cancellationToken = default);

    /// <summary>Get count of sibling items at the same level.</summary>
    Task<int> GetSiblingCountAsync(
        int navId,
        int? parentNavItemId,
        CancellationToken cancellationToken = default);

    // ─── Legacy menu access ─────────────────────────────────────────

    /// <summary>
    /// Get legacy menus for a job with hierarchical items.
    /// Reads from dbo.JobMenus / dbo.JobMenuItems.
    /// </summary>
    Task<List<NavEditorLegacyMenuDto>> GetLegacyMenusForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    // ─── Mutations ──────────────────────────────────────────────────

    /// <summary>Add a new nav to the context.</summary>
    void AddNav(Nav nav);

    /// <summary>Add a new nav item to the context.</summary>
    void AddNavItem(NavItem navItem);

    /// <summary>Remove a nav item from the context.</summary>
    void RemoveNavItem(NavItem navItem);

    /// <summary>Save all pending changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
