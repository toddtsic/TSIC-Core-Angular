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
    public required string Text { get; init; }  // Always non-null for rendered items (hide rows are filtered before this DTO is used)
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
/// Text is nullable because hide rows (DefaultNavItemId set, Active=false) carry no display text.
/// </summary>
public record NavEditorNavItemDto
{
    public required int NavItemId { get; init; }
    public required int NavId { get; init; }
    public int? ParentNavItemId { get; init; }
    public required int SortOrder { get; init; }
    public string? Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
    public required bool Active { get; init; }
    /// <summary>
    /// Set on job override items that suppress a platform default item (Active=false).
    /// </summary>
    public int? DefaultNavItemId { get; init; }
    /// <summary>
    /// Set on job override items that should be slotted under an existing default section.
    /// </summary>
    public int? DefaultParentNavItemId { get; init; }
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
/// For hide rows: set DefaultNavItemId + leave Text null.
/// For slotted additions: set DefaultParentNavItemId.
/// </summary>
public record CreateNavItemRequest
{
    public required int NavId { get; init; }
    public int? ParentNavItemId { get; init; }
    /// <summary>Set to suppress a platform default item (creates a hide row with Active=false).</summary>
    public int? DefaultNavItemId { get; init; }
    /// <summary>Set to slot this item under an existing default section.</summary>
    public int? DefaultParentNavItemId { get; init; }
    public string? Text { get; init; }
    public string? IconName { get; init; }
    public string? RouterLink { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }
}

/// <summary>
/// Update an existing nav item. Text is nullable to support hide rows.
/// </summary>
public record UpdateNavItemRequest
{
    public string? Text { get; init; }
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
/// Cascade a route change to all matching nav items across platform default navs.
/// Matches by text + parent text (same tree position) across all roles.
/// </summary>
public record CascadeRouteRequest
{
    /// <summary>The primary item that was just edited.</summary>
    public required int NavItemId { get; init; }

    /// <summary>New RouterLink value (null clears the route).</summary>
    public string? RouterLink { get; init; }

    /// <summary>New NavigateUrl value.</summary>
    public string? NavigateUrl { get; init; }

    /// <summary>New Target value.</summary>
    public string? Target { get; init; }
}

/// <summary>
/// Toggle the Active state of a nav.
/// </summary>
public record ToggleNavActiveRequest
{
    public required bool Active { get; init; }
}

/// <summary>
/// Move a Level 2 nav item to a different parent group within the same nav.
/// </summary>
public record MoveNavItemRequest
{
    /// <summary>The NavItemId of the target Level 1 parent to move the item under.</summary>
    public required int TargetParentNavItemId { get; init; }
}

/// <summary>
/// Clone a Level 1 nav item and its active children to another role's nav.
/// </summary>
public record CloneBranchRequest
{
    /// <summary>The NavItemId of the source Level 1 item to clone.</summary>
    public required int SourceNavItemId { get; init; }

    /// <summary>The NavId of the target nav (must be a different nav).</summary>
    public required int TargetNavId { get; init; }

    /// <summary>If true, replace an existing Level 1 item with the same text in the target.</summary>
    public required bool ReplaceExisting { get; init; }
}

/// <summary>
/// Returned by DeleteNavItemAsync when a default item has job override references.
/// The caller must re-request with force=true to proceed with cascade deletion.
/// </summary>
public record DeleteNavItemResult
{
    public required bool RequiresConfirmation { get; init; }
    public required string Message { get; init; }
    public required int AffectedCount { get; init; }
}

/// <summary>
/// Show or hide a platform default nav item for the current job.
/// Creates or removes a hide row in the job's override nav.
/// </summary>
public record ToggleHideRequest
{
    public required string RoleId { get; init; }
    public required int DefaultNavItemId { get; init; }
    public required bool Hide { get; init; }
}

/// <summary>Request body for ensuring a job override nav exists for a role.</summary>
public record EnsureJobOverrideNavRequest
{
    public required string RoleId { get; init; }
}
