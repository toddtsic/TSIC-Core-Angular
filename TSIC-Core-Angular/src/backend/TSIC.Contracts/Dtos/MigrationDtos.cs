using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Report of a profile metadata migration operation
/// </summary>
public class MigrationReport
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int WarningCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<MigrationResult> Results { get; set; } = new();
    public List<string> GlobalWarnings { get; set; } = new();
}

/// <summary>
/// Result for a single job migration
/// </summary>
public class MigrationResult
{
    [Required, JsonRequired]
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string ProfileType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public int FieldCount { get; set; }
    public ProfileMetadata? GeneratedMetadata { get; set; }
}

/// <summary>
/// Request to preview migration for a single job
/// </summary>
public class PreviewMigrationRequest
{
    [Required, JsonRequired]
    public Guid JobId { get; set; }
}

/// <summary>
/// Request to migrate all jobs
/// </summary>
public class MigrateAllRequest
{
    /// <summary>
    /// If true, only preview changes without committing to database
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Optional: filter to specific profile types (e.g., ["PP10", "PP17"])
    /// </summary>
    public List<string>? ProfileTypes { get; set; }
}
