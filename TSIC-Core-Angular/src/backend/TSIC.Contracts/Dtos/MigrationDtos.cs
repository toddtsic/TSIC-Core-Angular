using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Report of a profile metadata migration operation
/// </summary>
public record MigrationReport
{
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int WarningCount { get; init; }
    public required int SkippedCount { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
    public required List<MigrationResult> Results { get; init; } = new();
    public required List<string> GlobalWarnings { get; init; } = new();
}

/// <summary>
/// Result for a single job migration
/// </summary>
public record MigrationResult
{
    [Required, JsonRequired]
    public required Guid JobId { get; init; }
    public required string JobName { get; init; } = string.Empty;
    public required string ProfileType { get; init; } = string.Empty;
    public required bool Success { get; init; }
    public required string? ErrorMessage { get; init; }
    public required List<string> Warnings { get; init; } = new();
    public required int FieldCount { get; init; }
    public required ProfileMetadata? GeneratedMetadata { get; init; }
}

/// <summary>
/// Request to preview migration for a single job
/// </summary>
public record PreviewMigrationRequest
{
    [Required, JsonRequired]
    public required Guid JobId { get; init; }
}

/// <summary>
/// Request to migrate all jobs
/// </summary>
public record MigrateAllRequest
{
    /// <summary>
    /// If true, only preview changes without committing to database
    /// </summary>
    public required bool DryRun { get; init; }

    /// <summary>
    /// Optional: filter to specific profile types (e.g., ["PP10", "PP17"])
    /// </summary>
    public List<string>? ProfileTypes { get; init; }
}
