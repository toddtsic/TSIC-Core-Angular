using FluentValidation;

namespace TSIC.API.Dtos;

public record ClubRepRegistrationRequest
{
    public required string ClubName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }
    public required string Cellphone { get; init; }
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
    public List<ClubSearchResult>? SimilarClubs { get; init; }
}

public record ClubSearchResult
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public string? State { get; init; }
    public required int TeamCount { get; init; }
    public required int MatchScore { get; init; }
}
