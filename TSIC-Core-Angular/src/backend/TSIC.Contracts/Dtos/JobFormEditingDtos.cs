namespace TSIC.Contracts.Dtos;

/// <summary>
/// A job the per-job Profile Editor can target, with enough context for the job picker to label it
/// and warn about drift. <see cref="IsCustomized"/> is a best-effort flag: the job's normalized
/// player field set differs from its profile type's representative canonical (see
/// ProfileMetadataMigrationService for the comparison contract).
/// </summary>
public record EditableJobDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }

    /// <summary>Profile type parsed from CoreRegformPlayer (e.g. PP47), or null if unset.</summary>
    public string? ProfileType { get; init; }

    public required bool HasPlayerForm { get; init; }
    public required bool IsCustomized { get; init; }
}

/// <summary>
/// One job that a template-wide (fan-out) write would overwrite, flagged so the confirm modal can
/// call out jobs whose per-job customizations would be lost.
/// </summary>
public record AffectedJobDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public required bool IsCustomized { get; init; }
}

/// <summary>
/// Preview of the blast radius of a template-wide write: every job on the type, with counts, so the
/// UI can gate the write behind an explicit, informed confirmation.
/// </summary>
public record AffectedJobsResult
{
    public required string ProfileType { get; init; }
    public required int TotalCount { get; init; }
    public required int CustomizedCount { get; init; }
    public required List<AffectedJobDto> Jobs { get; init; }
}
