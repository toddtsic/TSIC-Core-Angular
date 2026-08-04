using TSIC.Domain.Adults;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// The canonical adult profile the CURRENT job is configured for, derived from its
/// <c>RegformName_Coach</c> — the same mapping Configure → Job's coach-form picker shows. Lets the
/// Profile Editor open on the job's own profile instead of the first in the list.
/// </summary>
public record CurrentJobAdultProfileDto
{
    public required Guid JobId { get; init; }
    public required string Profile { get; init; } = string.Empty;        // AC1 | AC2 | AC3
    public required string DisplayName { get; init; } = string.Empty;
    /// <summary>Whether this job collects a USA Lacrosse number, and whether it blocks registration.</summary>
    public required AdultUsLaxMode UsLax { get; init; }
    /// <summary>Whether this job has a materialized AdultProfileMetadataJson — i.e. whether editor saves reach it.</summary>
    public required bool IsMaterialized { get; init; }
}

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

// AdultProfileBatchMigrationReport and AdultMigrateAllRequest were removed with the bulk
// materialization endpoints. AdultProfileMigrationResult survives — it is still the return type of
// UpdateAdultProfileRoleAsync (the type-scoped Profile Editor save).
