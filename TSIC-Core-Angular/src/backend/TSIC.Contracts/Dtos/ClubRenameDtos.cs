using FluentValidation;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request to rename a club the authenticated user reps. Only honored while the
/// club has no registered teams (IsInUse=false) — the data-safe rename window.
/// </summary>
public record ClubRenameRequest
{
    /// <summary>The club's current name (used to resolve which of the rep's clubs to rename).</summary>
    public required string CurrentClubName { get; init; }

    /// <summary>The new club name.</summary>
    public required string NewClubName { get; init; }
}

/// <summary>
/// Result of a club rename. On failure, Message explains why (not your club,
/// teams already registered, name collision).
/// </summary>
public record ClubRenameResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>The persisted new name on success — clients should adopt this verbatim.</summary>
    public string? NewClubName { get; init; }
}

/// <summary>
/// SuperUser admin club rename. Unlike the rep-facing <see cref="ClubRenameRequest"/>, this is
/// allowed even once the club has registered teams — it recomposes every affected job's schedule.
/// Targets the club by id (the club is global/cross-customer; name is not a safe key).
/// </summary>
public record AdminClubRenameRequest
{
    public required int ClubId { get; init; }
    public required string NewClubName { get; init; }
}

/// <summary>A job that holds teams belonging to a club — the club-rename impact/scope unit.</summary>
public record ClubAffectedJob
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required int TeamCount { get; init; }
}

/// <summary>One job touched by an admin club rename, with the schedule-row counts it reported.</summary>
public record ClubRenameJobResult
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required int RowsExamined { get; init; }
    public required int RowsChanged { get; init; }
}

/// <summary>
/// Result of an admin club rename. On success, PerJob lists every job whose schedule was recomposed
/// (empty when the club has no scheduled teams). On a no-op (name unchanged) Success is true with an
/// empty PerJob.
/// </summary>
public record AdminClubRenameResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? NewClubName { get; init; }
    public IReadOnlyList<ClubRenameJobResult> PerJob { get; init; } = [];
}

public class AdminClubRenameRequestValidator : AbstractValidator<AdminClubRenameRequest>
{
    public AdminClubRenameRequestValidator()
    {
        RuleFor(x => x.ClubId).GreaterThan(0).WithMessage("A club id is required");
        RuleFor(x => x.NewClubName)
            .NotEmpty().WithMessage("New club name is required")
            .MaximumLength(255).WithMessage("Club name cannot exceed 255 characters");
    }
}

public class ClubRenameRequestValidator : AbstractValidator<ClubRenameRequest>
{
    public ClubRenameRequestValidator()
    {
        RuleFor(x => x.CurrentClubName)
            .NotEmpty().WithMessage("Current club name is required");

        RuleFor(x => x.NewClubName)
            .NotEmpty().WithMessage("New club name is required")
            .MaximumLength(255).WithMessage("Club name cannot exceed 255 characters");
    }
}
