namespace TSIC.Contracts.Dtos;

/// <summary>
/// Menu data for job-specific, role-based navigation.
/// Contains hierarchical menu items (max 2 levels: root + children).
/// </summary>
public record MenuDto
{
    public required Guid MenuId { get; init; }
    public required Guid JobId { get; init; }
    public string? RoleId { get; init; }
    public required int MenuTypeId { get; init; }
    public string? Tag { get; init; }
    public required List<MenuItemDto> Items { get; init; } = new();
}

/// <summary>
/// Individual menu item with support for hierarchical structure (parent-child).
/// Supports multiple link types: RouterLink (Angular), NavigateUrl (external), Controller+Action (legacy MVC).
/// </summary>
public record MenuItemDto
{
    public required Guid MenuItemId { get; init; }
    public Guid? ParentMenuItemId { get; init; }
    public int? Index { get; init; }
    public string? Text { get; init; }
    public string? IconName { get; init; }
    public required bool BCollapsed { get; init; }
    public required bool BTextWrap { get; init; }

    // Link types (precedence: NavigateUrl → RouterLink → Controller/Action)
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public string? LinkTarget { get; init; }

    /// <summary>
    /// Indicates whether the controller/action for this menu item exists in the backend.
    /// Set by RouteAvailabilityService during menu assembly.
    /// </summary>
    public required bool IsImplemented { get; init; }

    // Hierarchical structure
    public required List<MenuItemDto> Children { get; init; } = new();
}
