using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for nav editor operations — CRUD for platform defaults
/// and job overrides, plus legacy menu import.
/// </summary>
public interface INavEditorService
{
    /// <summary>Get all platform default navs with items.</summary>
    Task<List<NavEditorNavDto>> GetAllDefaultsAsync(CancellationToken ct = default);

    /// <summary>Get legacy menus for the current job (read-only, for import panel).</summary>
    Task<List<NavEditorLegacyMenuDto>> GetLegacyMenusAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Create a platform default nav for a role (or job override if jobId set).</summary>
    Task<NavEditorNavDto> CreateNavAsync(CreateNavRequest request, string userId, CancellationToken ct = default);

    /// <summary>Create a new nav item within an existing nav.</summary>
    Task<NavEditorNavItemDto> CreateNavItemAsync(CreateNavItemRequest request, string userId, CancellationToken ct = default);

    /// <summary>Update an existing nav item.</summary>
    Task<NavEditorNavItemDto> UpdateNavItemAsync(int navItemId, UpdateNavItemRequest request, string userId, CancellationToken ct = default);

    /// <summary>Delete a nav item (hard or soft depending on sibling count).</summary>
    Task DeleteNavItemAsync(int navItemId, CancellationToken ct = default);

    /// <summary>Reorder sibling nav items.</summary>
    Task ReorderNavItemsAsync(ReorderNavItemsRequest request, string userId, CancellationToken ct = default);

    /// <summary>
    /// Import legacy menu items into the new nav structure.
    /// Translates Controller/Action to RouterLink where possible.
    /// </summary>
    Task<NavEditorNavDto> ImportLegacyMenuAsync(ImportLegacyMenuRequest request, string userId, CancellationToken ct = default);

    /// <summary>Toggle the Active state of a nav.</summary>
    Task ToggleNavActiveAsync(int navId, bool active, CancellationToken ct = default);

    /// <summary>
    /// Ensure platform default navs exist for all standard roles.
    /// Returns the count of newly created navs.
    /// </summary>
    Task<int> EnsureAllRoleNavsAsync(string userId, CancellationToken ct = default);
}
