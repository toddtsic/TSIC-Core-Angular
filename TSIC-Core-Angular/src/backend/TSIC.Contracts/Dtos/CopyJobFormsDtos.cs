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

    /// <summary>
    /// The job to copy INTO. Null ⇒ the caller's current job (resolved from the JWT regId),
    /// preserving the original "copy onto this job" behavior. Non-null targets an arbitrary job
    /// (SuperUser only) so a form can be poured into a brand-new job without logging into it.
    /// </summary>
    public Guid? TargetJobId { get; init; }

    /// <summary>Copy the source's <c>PlayerProfileMetadataJson</c>.</summary>
    public bool IncludePlayer { get; init; }

    /// <summary>Copy the source's <c>AdultProfileMetadataJson</c> (all three adult roles at once).</summary>
    public bool IncludeCoach { get; init; }

    /// <summary>
    /// Also copy the source's profile-type pointer (<c>CoreRegformPlayer</c>). Requires
    /// <see cref="IncludePlayer"/> — the pointer and the player form it points at travel together.
    /// </summary>
    public bool IncludePointer { get; init; }

    /// <summary>Also copy the source's per-job dropdown option sets (<c>JsonOptions</c>).</summary>
    public bool IncludeOptions { get; init; }
}

/// <summary>Outcome of a copy-forms operation.</summary>
public record CopyJobFormsResult
{
    public required bool Success { get; init; }
    public required bool PlayerCopied { get; init; }
    public required bool CoachCopied { get; init; }
    public bool PointerCopied { get; init; }
    public bool OptionsCopied { get; init; }
    public required string SourceJobName { get; init; } = string.Empty;
    public string? TargetJobName { get; init; }
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
