namespace TSIC.Contracts.Dtos;

// ── Read DTOs ────────────────────────────────────────────────

/// <summary>
/// Merged nav for a role+job. Contains platform defaults merged with
/// any job-specific override items.
/// </summary>
public record NavDto
{
    public required int NavId { get; init; }
    public required string RoleId { get; init; }
    public Guid? JobId { get; init; }
    public required bool Active { get; init; }
    public required List<NavItemDto> Items { get; init; } = new();
}

/// <summary>
/// Nav item with children (max 2 levels: root + children).
/// </summary>
public record NavItemDto
{
    public required int NavItemId { get; init; }
    public int? ParentNavItemId { get; init; }
    public required int SortOrder { get; init; }
    public required string Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
    public required bool Active { get; init; }
    public required List<NavItemDto> Children { get; init; } = new();
}

// ── Editor DTOs ──────────────────────────────────────────────

/// <summary>
/// Platform default or job override nav for the editor view.
/// Includes role name and all items (active + inactive).
/// </summary>
public record NavEditorNavDto
{
    public required int NavId { get; init; }
    public required string RoleId { get; init; }
    public string? RoleName { get; init; }
    public Guid? JobId { get; init; }
    public required bool Active { get; init; }
    public required bool IsDefault { get; init; }
    public required List<NavEditorNavItemDto> Items { get; init; } = new();
}

/// <summary>
/// Nav item for the editor — includes all fields for editing.
/// </summary>
public record NavEditorNavItemDto
{
    public required int NavItemId { get; init; }
    public required int NavId { get; init; }
    public int? ParentNavItemId { get; init; }
    public required int SortOrder { get; init; }
    public required string Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
    public required bool Active { get; init; }
    public required List<NavEditorNavItemDto> Children { get; init; } = new();
}

/// <summary>
/// Legacy menu read-only view for the transitional migration panel.
/// </summary>
public record NavEditorLegacyMenuDto
{
    public required Guid MenuId { get; init; }
    public required Guid JobId { get; init; }
    public string? RoleId { get; init; }
    public string? RoleName { get; init; }
    public required bool Active { get; init; }
    public required List<NavEditorLegacyItemDto> Items { get; init; } = new();
}

/// <summary>
/// Legacy menu item for the migration panel. Includes Controller/Action
/// for display so SuperUser can see legacy routing.
/// </summary>
public record NavEditorLegacyItemDto
{
    public required Guid MenuItemId { get; init; }
    public Guid? ParentMenuItemId { get; init; }
    public int? Index { get; init; }
    public string? Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public string? Target { get; init; }
    public required bool Active { get; init; }
    public required List<NavEditorLegacyItemDto> Children { get; init; } = new();
}

// ── Request DTOs ─────────────────────────────────────────────

/// <summary>
/// Create a new platform default nav (JobId omitted) or job override (JobId set).
/// </summary>
public record CreateNavRequest
{
    public required string RoleId { get; init; }
    public Guid? JobId { get; init; }
}

/// <summary>
/// Create a new nav item within an existing nav.
/// </summary>
public record CreateNavItemRequest
{
    public required int NavId { get; init; }
    public int? ParentNavItemId { get; init; }
    public required string Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
}

/// <summary>
/// Update an existing nav item.
/// </summary>
public record UpdateNavItemRequest
{
    public required string Text { get; init; }
    public required bool Active { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
}

/// <summary>
/// Reorder sibling nav items within a parent (or root level).
/// </summary>
public record ReorderNavItemsRequest
{
    public required int NavId { get; init; }
    public int? ParentNavItemId { get; init; }
    public required List<int> OrderedItemIds { get; init; } = new();
}

/// <summary>
/// Import legacy menu items into the new nav structure.
/// </summary>
public record ImportLegacyMenuRequest
{
    public required Guid SourceMenuId { get; init; }
    public required string TargetRoleId { get; init; }
}

/// <summary>
/// Toggle the Active state of a nav.
/// </summary>
public record ToggleNavActiveRequest
{
    public required bool Active { get; init; }
}
