namespace TSIC.Contracts.Dtos;

/// <summary>
/// Projection for GetAllMenusForJobAsync — flattens Role.Name into RoleName.
/// Avoids loading full AspNetRoles entity.
/// </summary>
public record MenuListProjection
{
    public required Guid MenuId { get; init; }
    public required Guid JobId { get; init; }
    public string? RoleId { get; init; }
    public string? RoleName { get; init; }
    public required bool Active { get; init; }
    public required int MenuTypeId { get; init; }
}

/// <summary>
/// Admin view of a role menu with nested items tree.
/// Includes inactive items for full admin visibility.
/// </summary>
public record MenuAdminDto
{
    public required Guid MenuId { get; init; }
    public required Guid JobId { get; init; }
    public string? RoleId { get; init; }
    public string? RoleName { get; init; }
    public required bool Active { get; init; }
    public required int MenuTypeId { get; init; }
    public required List<MenuItemAdminDto> Items { get; init; } = new();
}

/// <summary>
/// Admin view of a menu item with full details and nested children.
/// </summary>
public record MenuItemAdminDto
{
    public required Guid MenuItemId { get; init; }
    public required Guid MenuId { get; init; }
    public Guid? ParentMenuItemId { get; init; }
    public string? Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public string? Target { get; init; }
    public required bool Active { get; init; }
    public int? Index { get; init; }
    public required List<MenuItemAdminDto> Children { get; init; } = new();
}

/// <summary>
/// Request to create a new menu item.
/// </summary>
public record CreateMenuItemRequest
{
    public required Guid MenuId { get; init; }
    public Guid? ParentMenuItemId { get; init; }
    public required string Text { get; init; }
    public required bool Active { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public string? Target { get; init; }
}

/// <summary>
/// Request to update an existing menu item.
/// </summary>
public record UpdateMenuItemRequest
{
    public required string Text { get; init; }
    public required bool Active { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public string? Target { get; init; }
}

/// <summary>
/// Request to toggle a menu's active state.
/// </summary>
public record UpdateMenuActiveRequest
{
    public required bool Active { get; init; }
}

/// <summary>
/// Request to reorder sibling menu items.
/// </summary>
public record ReorderMenuItemsRequest
{
    public required Guid MenuId { get; init; }
    public Guid? ParentMenuItemId { get; init; }
    public required List<Guid> OrderedItemIds { get; init; } = new();
}
