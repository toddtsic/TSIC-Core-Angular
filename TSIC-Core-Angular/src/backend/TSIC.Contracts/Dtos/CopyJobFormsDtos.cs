namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request to copy another job's player and/or adult (coach) form definition onto the current job.
/// Form-JSON only — the runtime renders registration forms from the materialized metadata, so a copy
/// of <c>PlayerProfileMetadataJson</c> / <c>AdultProfileMetadataJson</c> is sufficient for the forms
/// to work on the target job.
/// </summary>
public record CopyJobFormsRequest
{
    /// <summary>The job to copy form definitions FROM.</summary>
    public required Guid SourceJobId { get; init; }

    /// <summary>Copy the source's <c>PlayerProfileMetadataJson</c>.</summary>
    public bool IncludePlayer { get; init; }

    /// <summary>Copy the source's <c>AdultProfileMetadataJson</c> (all three adult roles at once).</summary>
    public bool IncludeCoach { get; init; }
}

/// <summary>Outcome of a copy-forms operation.</summary>
public record CopyJobFormsResult
{
    public required bool Success { get; init; }
    public required bool PlayerCopied { get; init; }
    public required bool CoachCopied { get; init; }
    public required string SourceJobName { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// A job that can serve as a copy source, flagged with which form(s) it actually carries so the
/// picker can badge/disable choices. The current job is excluded server-side (can't copy from itself).
/// </summary>
public record CopyFormSourceDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public required bool HasPlayerForm { get; init; }
    public required bool HasCoachForm { get; init; }
}
