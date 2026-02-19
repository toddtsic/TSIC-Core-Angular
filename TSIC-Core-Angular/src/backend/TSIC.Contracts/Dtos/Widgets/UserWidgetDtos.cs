namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// A single entry in the user's widget customization delta.
/// </summary>
public record UserWidgetEntryDto
{
    public required int WidgetId { get; init; }
    public required int CategoryId { get; init; }
    public required int DisplayOrder { get; init; }
    public required bool IsHidden { get; init; }
    public string? Config { get; init; }
}

/// <summary>
/// Request to save user widget customizations (replace-all pattern).
/// </summary>
public record SaveUserWidgetsRequest
{
    public required List<UserWidgetEntryDto> Entries { get; init; }
}

/// <summary>
/// Available widget for the user's role/jobType — used by the library browser.
/// </summary>
public record AvailableWidgetDto
{
    public required int WidgetId { get; init; }
    public required string Name { get; init; }
    public required string WidgetType { get; init; }
    public required string ComponentKey { get; init; }
    public required string? Description { get; init; }
    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required string Workspace { get; init; }
    public required bool IsVisible { get; init; }
}
