using FluentValidation;

namespace TSIC.Application.Validators
{
    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required");
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters");
        }
    }

    // DTO for validation
    public record LoginRequest
    {
        public required string Username { get; init; }
        public required string Password { get; init; }
    }
}
