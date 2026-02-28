namespace TSIC.Contracts.Dtos.Ladt;

public record DivisionDetailDto
{
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record CreateDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record UpdateDivisionRequest
{
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

// ── Division Name Sync ──

public record DivisionNameSyncRequest
{
    public required List<string> ThemeNames { get; init; }
}

public record DivisionNameSyncPreview
{
    public required string AgegroupName { get; init; }
    public required Guid AgegroupId { get; init; }
    public required int DivisionCount { get; init; }
    public required List<DivisionRenameEntry> Divisions { get; init; }
}

public record DivisionRenameEntry
{
    public required Guid DivId { get; init; }
    public required string CurrentName { get; init; }
    public required string ProposedName { get; init; }
}

public record DivisionNameSyncResult
{
    public required int DivisionsRenamed { get; init; }
    public required List<string> Errors { get; init; }
}
