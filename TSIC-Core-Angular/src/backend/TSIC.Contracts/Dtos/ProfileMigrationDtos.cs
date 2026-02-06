namespace TSIC.Contracts.Dtos;

/// <summary>
/// Summary of a profile type and its usage across jobs
/// </summary>
public record ProfileSummary
{
    public required string ProfileType { get; init; } = string.Empty;
    public required int JobCount { get; init; }
    public required int MigratedJobCount { get; init; }
    public required bool AllJobsMigrated { get; init; }
    public required List<string> SampleJobNames { get; init; } = new();
}

/// <summary>
/// Result of migrating a single profile type across all jobs
/// </summary>
public record ProfileMigrationResult
{
    public required string ProfileType { get; init; } = string.Empty;
    public required bool Success { get; init; }
    public required int FieldCount { get; init; }
    public required int JobsAffected { get; init; }
    public required List<Guid> AffectedJobIds { get; init; } = new();
    public required List<string> AffectedJobNames { get; init; } = new();
    public required List<string> AffectedJobYears { get; init; } = new();
    public required ProfileMetadata? GeneratedMetadata { get; init; }
    public required List<string> Warnings { get; init; } = new();
    public required string? ErrorMessage { get; init; }
}

/// <summary>
/// Report for batch profile migrations
/// </summary>
public record ProfileBatchMigrationReport
{
    public required DateTime StartedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
    public required int TotalProfiles { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int TotalJobsAffected { get; init; }
    public required List<ProfileMigrationResult> Results { get; init; } = new();
    public required List<string> GlobalWarnings { get; init; } = new();
}

/// <summary>
/// Request to migrate multiple profiles
/// </summary>
public record MigrateProfilesRequest
{
    public required bool DryRun { get; init; } = true;
    public List<string>? ProfileTypes { get; init; }
}

/// <summary>
/// Result of testing field validation
/// </summary>
public record ValidationTestResult
{
    public required string FieldName { get; init; } = string.Empty;
    public required string TestValue { get; init; } = string.Empty;
    public required bool IsValid { get; init; }
    public required List<string> Messages { get; init; } = new();
}

/// <summary>
/// Request to clone an existing profile for the current job
/// JobId is determined from regId claim in JWT token
/// </summary>
public record CloneProfileRequest
{
    public required string SourceProfileType { get; init; } = string.Empty;
}

/// <summary>
/// Result of cloning a profile
/// </summary>
public record CloneProfileResult
{
    public required bool Success { get; init; }
    public required string NewProfileType { get; init; } = string.Empty;
    public required string SourceProfileType { get; init; } = string.Empty;
    public required int FieldCount { get; init; }
    public required string? ErrorMessage { get; init; }
}

/// <summary>
/// Result when asking server for the next profile type name for a given family
/// </summary>
public record NextProfileTypeResult
{
    public required string NewProfileType { get; init; } = string.Empty;
}

/// <summary>
/// Profile metadata enriched with job-specific JsonOptions
/// Used for previewing how a form will appear for a specific job
/// </summary>
public record ProfileMetadataWithOptions
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; } = string.Empty;
    public required ProfileMetadata Metadata { get; init; } = new();
    public Dictionary<string, object>? JsonOptions { get; init; }
}

/// <summary>
/// Aggregated domain entry for all known profile fields across profiles/jobs
/// Useful for generating a static allowed field list for the editor
/// </summary>
public record AllowedFieldDomainItem
{
    public required string Name { get; init; } = string.Empty;
    public required string DisplayName { get; init; } = string.Empty;
    public required string DefaultInputType { get; init; } = string.Empty; // e.g., TEXT, SELECT, CHECKBOX, HIDDEN
    public required string DefaultVisibility { get; init; } = "public";   // public | adminOnly | hidden
    public required int SeenInProfiles { get; init; }
}
