using TSIC.Contracts.Dtos;

namespace TSIC.Application.Services.MenuAdmin;

/// <summary>
/// Service for menu administration operations.
/// Manages job-specific, role-based menus (Level 0 = menu, Level 1 = parent items, Level 2 = child items).
/// </summary>
public interface IMenuAdminService
{
    /// <summary>
    /// Gets all menus for a job with their nested item hierarchy.
    /// Includes inactive items (admins need full visibility).
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <returns>List of menus with nested parent/child items</returns>
    Task<List<MenuAdminDto>> GetAllMenusAsync(Guid jobId);

    /// <summary>
    /// Toggles the Active state of a menu (Level 0).
    /// </summary>
    /// <param name="menuId">Menu identifier</param>
    /// <param name="active">New active state</param>
    /// <param name="userId">User making the change</param>
    Task ToggleMenuActiveAsync(Guid menuId, bool active, string userId);

    /// <summary>
    /// Creates a new menu item.
    /// If ParentMenuItemId is null (Level 1): creates parent + auto-creates stub child.
    /// If ParentMenuItemId is set (Level 2): creates child with Index = siblingCount + 1.
    /// </summary>
    /// <param name="jobId">Job identifier (for validation)</param>
    /// <param name="request">Item creation details</param>
    /// <param name="userId">User making the change</param>
    /// <returns>Created menu item with children (for Level 1 parent)</returns>
    Task<MenuItemAdminDto> CreateMenuItemAsync(Guid jobId, CreateMenuItemRequest request, string userId);

    /// <summary>
    /// Updates an existing menu item's properties.
    /// MenuId and ParentMenuItemId cannot be changed.
    /// </summary>
    /// <param name="menuItemId">Item identifier</param>
    /// <param name="request">Updated properties</param>
    /// <param name="userId">User making the change</param>
    /// <returns>Updated menu item</returns>
    Task<MenuItemAdminDto> UpdateMenuItemAsync(Guid menuItemId, UpdateMenuItemRequest request, string userId);

    /// <summary>
    /// Deletes a menu item.
    /// If siblingCount > 1: hard delete.
    /// If siblingCount == 1: soft delete (sets Active=false) to prevent orphaning parent.
    /// </summary>
    /// <param name="menuItemId">Item identifier</param>
    Task DeleteMenuItemAsync(Guid menuItemId);

    /// <summary>
    /// Reorders sibling menu items by assigning sequential Index values.
    /// </summary>
    /// <param name="request">Menu ID, optional parent ID, and ordered list of item IDs</param>
    /// <param name="userId">User making the change</param>
    Task ReorderMenuItemsAsync(ReorderMenuItemsRequest request, string userId);

    /// <summary>
    /// Ensures all 6 standard roles have menus for a job.
    /// Creates missing role menus with MenuTypeId=PerLoginRole, Active=false,
    /// plus stub parent item ("New Parent", Index=1) and stub child ("new child", Index=1).
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <param name="userId">User making the change</param>
    /// <returns>Number of menus created</returns>
    Task<int> EnsureAllRoleMenusAsync(Guid jobId, string userId);
}
