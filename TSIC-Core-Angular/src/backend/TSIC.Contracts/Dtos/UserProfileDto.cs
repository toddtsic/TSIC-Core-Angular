using FluentValidation;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Current profile data for any authenticated user (role-neutral). Drives the
/// self-service profile-review/edit surfaces — e.g. the adult registration
/// wizard's account-summary step. Identity-mirror of <see cref="ClubRepProfileDto"/>,
/// kept role-neutral so it isn't tied to the club-rep flow.
/// </summary>
public record UserProfileDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Cellphone { get; init; }
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }
}

/// <summary>
/// Request to update the authenticated user's profile fields. Excludes
/// username/password (handled via dedicated flows). Field set is a 1:1 match of
/// <see cref="ClubRepProfileUpdateRequest"/> so the club-rep flow can delegate
/// here with no behavior change. Identity fields (first/last name) are carried
/// for that parity but are locked on the self-service edit surfaces — see the
/// hand-off rationale on the club-rep edit form.
/// </summary>
public record UserProfileUpdateRequest
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Cellphone { get; init; }
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }
}

public class UserProfileUpdateRequestValidator : AbstractValidator<UserProfileUpdateRequest>
{
    public UserProfileUpdateRequestValidator()
    {
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

        RuleFor(x => x.Cellphone)
            .NotEmpty().WithMessage("Cell phone is required")
            .Matches(@"^[0-9\s\-\(\)\+]+$").WithMessage("Invalid phone number format");

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
    }
}
