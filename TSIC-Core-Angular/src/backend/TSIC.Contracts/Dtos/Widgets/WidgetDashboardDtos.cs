namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// Full dashboard response: widgets grouped by workspace and category.
/// </summary>
public record WidgetDashboardResponse
{
    public required List<WidgetWorkspaceDto> Workspaces { get; init; }
}

/// <summary>
/// A workspace (dashboard, player-reg, scheduling, etc.) containing categorized widget groups.
/// </summary>
public record WidgetWorkspaceDto
{
    public required string Workspace { get; init; }
    public required List<WidgetCategoryGroupDto> Categories { get; init; }
}

/// <summary>
/// A category group within a section, containing its widgets.
/// </summary>
public record WidgetCategoryGroupDto
{
    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required string? Icon { get; init; }
    public required int DisplayOrder { get; init; }
    public required List<WidgetItemDto> Widgets { get; init; }
}

/// <summary>
/// A single widget item (merged from defaults + per-job overrides).
/// </summary>
public record WidgetItemDto
{
    public required int WidgetId { get; init; }
    public required string Name { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public required int DisplayOrder { get; init; }
    public required string? Config { get; init; }
    public required string? Description { get; init; }
    public required bool IsOverridden { get; init; }
}

/// <summary>
/// Projection for WidgetDefault and JobWidget repository queries.
/// Flattens Widget + Category navigations into a single row.
/// </summary>
public record WidgetItemProjection
{
    public required int WidgetId { get; init; }
    public required int CategoryId { get; init; }
    public required int DisplayOrder { get; init; }
    public string? Config { get; init; }
    public required bool IsEnabled { get; init; }
    public required string WidgetName { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public string? Description { get; init; }
    public required string CategoryName { get; init; }
    public string? CategoryIcon { get; init; }
    public required int CategoryDefaultOrder { get; init; }
    public required string Workspace { get; init; }
}
