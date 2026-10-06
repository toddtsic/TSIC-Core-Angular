using FluentValidation;

namespace TSIC.Contracts.Dtos;

public record ClubRepRegistrationRequest
{
    public required string ClubName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Gender { get; init; }
    public required string Email { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }
    public required string Cellphone { get; init; }

    /// <summary>
    /// True when the registrant has checked the Terms of Service acceptance box.
    /// Required to be true at service time (mirrors adult registration pattern).
    /// On success the service stamps AspNetUsers.bTSICWaiverSigned + TSICWaiverSigned_TS.
    /// </summary>
    public required bool AcceptedTos { get; init; }
}

public class ClubRepRegistrationRequestValidator : AbstractValidator<ClubRepRegistrationRequest>
{
    public ClubRepRegistrationRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("Gender is required")
            .Must(g => g is "M" or "F").WithMessage("Gender must be M or F");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters");

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .Matches(@"^[a-zA-Z0-9._-]+$").WithMessage("Username can only contain letters, numbers, dots, underscores, and hyphens");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters")
            .MaximumLength(100).WithMessage("Password cannot exceed 100 characters");

        RuleFor(x => x.StreetAddress)
            .NotEmpty().WithMessage("Street address is required")
            .MaximumLength(200).WithMessage("Street address cannot exceed 200 characters");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required")
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters");

        RuleFor(x => x.State)
            .NotEmpty().WithMessage("State is required")
            .Length(2).WithMessage("State must be 2-letter code");

        RuleFor(x => x.PostalCode)
            .NotEmpty().WithMessage("Postal code is required")
            .Matches(@"^\d{5}(-\d{4})?$").WithMessage("Invalid postal code format");

        RuleFor(x => x.Cellphone)
            .NotEmpty().WithMessage("Cell phone is required")
            .Matches(@"^[0-9\s\-\(\)\+]+$").WithMessage("Invalid phone number format");
    }
}

public record ClubRepRegistrationResponse
{
    public required bool Success { get; init; }
    public int? ClubId { get; init; }
    public string? UserId { get; init; }
    public string? Message { get; init; }
}

public record ClubSearchResult
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public string? State { get; init; }
    public required int TeamCount { get; init; }
    public required int MatchScore { get; init; }

    /// <summary>
    /// True when this club shares a root organization name with the query
    /// (mega-club pattern, e.g. "3 Point Lacrosse - VA" vs "3 Point Lacrosse - NC").
    /// </summary>
    public bool IsRelatedClub { get; init; }

    /// <summary>
    /// True when this club's normalized name is identical to the query's
    /// (token sets match — covers exact text, case/whitespace differences,
    /// filler-only suffixes like "LC", and word reordering). Informational:
    /// a same-name club never refuses sign-up.
    /// </summary>
    public bool IsExactMatch { get; init; }

    /// <summary>
    /// True when this club is an EMPTY SHELL — no reps linked and no library teams.
    /// Sign-up claims such a club silently when the typed name is exactly its name;
    /// the server re-checks on the write, so this flag is never the gate.
    /// </summary>
    public bool IsClaimable { get; init; }

    // No rep name or email: the club search is anonymous (sign-up runs before login), and a
    // public type-ahead returning them handed out every club rep's contact details.
}
