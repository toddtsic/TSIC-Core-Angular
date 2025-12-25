namespace TSIC.Contracts.Dtos;

/// <summary>
/// Menu data for job-specific, role-based navigation.
/// Contains hierarchical menu items (max 2 levels: root + children).
/// </summary>
public class MenuDto
{
    public required Guid MenuId { get; set; }
    public required Guid JobId { get; set; }
    public string? RoleId { get; set; }
    public required int MenuTypeId { get; set; }
    public string? Tag { get; set; }
    public required List<MenuItemDto> Items { get; set; } = new();
}

/// <summary>
/// Individual menu item with support for hierarchical structure (parent-child).
/// Supports multiple link types: RouterLink (Angular), NavigateUrl (external), Controller+Action (legacy MVC).
/// </summary>
public class MenuItemDto
{
    public required Guid MenuItemId { get; set; }
    public Guid? ParentMenuItemId { get; set; }
    public int? Index { get; set; }
    public string? Text { get; set; }
    public string? IconName { get; set; }
    public bool BCollapsed { get; set; }
    public bool BTextWrap { get; set; }

    // Link types (precedence: NavigateUrl → RouterLink → Controller/Action)
    public string? RouterLink { get; set; }
    public string? NavigateUrl { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public string? LinkTarget { get; set; }

    // Hierarchical structure
    public List<MenuItemDto> Children { get; set; } = new();
}
