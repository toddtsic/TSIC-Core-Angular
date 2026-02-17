namespace TSIC.Contracts.Dtos.Widgets;

// ══════════════════════════════════════
// Reference Data (dropdowns / headers)
// ══════════════════════════════════════

/// <summary>
/// Job type for the JobType dropdown selector.
/// </summary>
public record JobTypeRefDto
{
    public required int JobTypeId { get; init; }
    public required string JobTypeName { get; init; }
}

/// <summary>
/// Role for matrix column headers.
/// </summary>
public record RoleRefDto
{
    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
}

/// <summary>
/// Widget category for workspace grouping.
/// </summary>
public record WidgetCategoryRefDto
{
    public required int CategoryId { get; init; }
    public required string Name { get; init; }
    public required string Workspace { get; init; }
    public string? Icon { get; init; }
    public required int DefaultOrder { get; init; }
}

// ══════════════════════════════════════
// Widget Definition CRUD
// ══════════════════════════════════════

/// <summary>
/// A registered widget definition with its category context.
/// </summary>
public record WidgetDefinitionDto
{
    public required int WidgetId { get; init; }
    public required string Name { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public required int CategoryId { get; init; }
    public string? Description { get; init; }
    public required string CategoryName { get; init; }
    public required string Workspace { get; init; }
}

/// <summary>
/// Request to register a new widget definition.
/// </summary>
public record CreateWidgetRequest
{
    public required string Name { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public required int CategoryId { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request to update an existing widget definition.
/// </summary>
public record UpdateWidgetRequest
{
    public required string Name { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public required int CategoryId { get; init; }
    public string? Description { get; init; }
}

// ══════════════════════════════════════
// Widget Default Matrix
// ══════════════════════════════════════

/// <summary>
/// A single cell in the defaults matrix: (Widget, Role, Category) with ordering and config.
/// </summary>
public record WidgetDefaultEntryDto
{
    public required int WidgetId { get; init; }
    public required string RoleId { get; init; }
    public required int CategoryId { get; init; }
    public required int DisplayOrder { get; init; }
    public string? Config { get; init; }
}

/// <summary>
/// Full defaults matrix for a single JobType.
/// </summary>
public record WidgetDefaultMatrixResponse
{
    public required int JobTypeId { get; init; }
    public required List<WidgetDefaultEntryDto> Entries { get; init; }
}

/// <summary>
/// Request to bulk-replace all defaults for a JobType.
/// </summary>
public record SaveWidgetDefaultsRequest
{
    public required int JobTypeId { get; init; }
    public required List<WidgetDefaultEntryDto> Entries { get; init; }
}

// ══════════════════════════════════════
// Widget-centric bulk assignment
// ══════════════════════════════════════

/// <summary>
/// A single (JobType, Role) assignment for a widget.
/// </summary>
public record WidgetAssignmentDto
{
    public required int JobTypeId { get; init; }
    public required string RoleId { get; init; }
}

/// <summary>
/// Current assignments for a single widget across all job types.
/// </summary>
public record WidgetAssignmentsResponse
{
    public required int WidgetId { get; init; }
    public required int CategoryId { get; init; }
    public required List<WidgetAssignmentDto> Assignments { get; init; }
}

/// <summary>
/// Request to bulk-replace all assignments for a single widget across job types.
/// </summary>
public record SaveWidgetAssignmentsRequest
{
    public required int WidgetId { get; init; }
    public required int CategoryId { get; init; }
    public required List<WidgetAssignmentDto> Assignments { get; init; }
}
