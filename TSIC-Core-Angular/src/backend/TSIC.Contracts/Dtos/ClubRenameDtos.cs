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
