namespace TSIC.Contracts.Dtos;

/// <summary>
/// Summary of one canonical adult coach profile (AC1/AC2) and its usage across jobs. The adult analog of
/// <see cref="ProfileSummary"/>; profiles are OUR nomenclature, mapped from legacy <c>RegformName_Coach</c>.
/// </summary>
public record AdultProfileSummary
{
    public required string Profile { get; init; } = string.Empty;        // AC1 | AC2
    public required string DisplayName { get; init; } = string.Empty;
    public required int JobCount { get; init; }
    /// <summary>How many of this profile's jobs carry the USLax capability (required sportAssnId).</summary>
    public required int UsLaxJobCount { get; init; }
    public required int MigratedJobCount { get; init; }
    public required bool AllJobsMigrated { get; init; }
    public required List<string> SampleJobNames { get; init; } = new();
}

/// <summary>
/// Result of materializing one canonical adult profile across all its jobs. The adult analog of
/// <see cref="ProfileMigrationResult"/>. Because USLax is a per-job capability, the preview surfaces both the
/// base role set and (when any job needs it) the USLax variant.
/// </summary>
public record AdultProfileMigrationResult
{
    public required string Profile { get; init; } = string.Empty;
    public required string DisplayName { get; init; } = string.Empty;
    public required bool Success { get; init; }
    public required int JobsAffected { get; init; }
    /// <summary>Of the affected jobs, how many were materialized with the USLax capability.</summary>
    public required int UsLaxJobsAffected { get; init; }
    public required List<Guid> AffectedJobIds { get; init; } = new();
    public required List<string> AffectedJobNames { get; init; } = new();
    public required List<string> AffectedJobYears { get; init; } = new();
    /// <summary>Representative base role set (no USLax) for this profile.</summary>
    public required AdultRoleMetadataSet? GeneratedMetadata { get; init; }
    /// <summary>Representative USLax role set (with required sportAssnId); null when no job needs it.</summary>
    public AdultRoleMetadataSet? GeneratedMetadataUsLax { get; init; }
    public required List<string> Warnings { get; init; } = new();
    public required string? ErrorMessage { get; init; }
}

/// <summary>Batch report for adult profile materialization. Adult analog of <see cref="ProfileBatchMigrationReport"/>.</summary>
public record AdultProfileBatchMigrationReport
{
    public required DateTime StartedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
    public required int TotalProfiles { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int TotalJobsAffected { get; init; }
    public required List<AdultProfileMigrationResult> Results { get; init; } = new();
    public required List<string> GlobalWarnings { get; init; } = new();
}

/// <summary>Request to materialize adult profiles. Adult analog of <see cref="MigrateProfilesRequest"/>, plus a force flag.</summary>
public record AdultMigrateAllRequest
{
    public required bool DryRun { get; init; } = true;
    /// <summary>When true, re-materialize jobs that already have AdultProfileMetadataJson (default: skip them).</summary>
    public bool Force { get; init; }
    /// <summary>Optional profile filter (AC1/AC2). Null/empty = all profiles.</summary>
    public List<string>? Profiles { get; init; }
}
