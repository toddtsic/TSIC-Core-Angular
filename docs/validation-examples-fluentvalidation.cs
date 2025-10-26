// FluentValidation approach - much more powerful for complex business rules
using FluentValidation;
using TSIC.Application.Services;

namespace TSIC.Application.Validators
{
    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        private readonly IRoleLookupService _roleLookupService;

        public LoginRequestValidator(IRoleLookupService roleLookupService)
        {
            _roleLookupService = roleLookupService;

            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("Username is required")
                .Length(3, 50).WithMessage("Username must be between 3 and 50 characters")
                .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("Username can only contain letters, numbers, and underscores")
                .Must(BeValidUser).WithMessage("Invalid username or password combination");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters")
                .Must((request, password) => BeValidCredentials(request.Username, password))
                    .WithMessage("Invalid username or password combination");
        }

        private bool BeValidUser(string username)
        {
            // Could check if user exists in database
            return !string.IsNullOrEmpty(username);
        }

        private bool BeValidCredentials(string username, string password)
        {
            // Could validate against database
            // This is where you could add complex business rules
            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        }
    }

    // Example of a more complex validator for team registration
    public class TeamRegistrationValidator : AbstractValidator<TeamRegistrationRequest>
    {
        public TeamRegistrationValidator()
        {
            RuleFor(x => x.TeamName)
                .NotEmpty()
                .Length(2, 100)
                .Matches(@"^[a-zA-Z0-9\s\-]+$");

            RuleFor(x => x.AgeGroup)
                .NotEmpty()
                .Must(BeValidAgeGroup);

            RuleFor(x => x.PlayerCount)
                .InclusiveBetween(5, 25)
                .When(x => x.AgeGroup.Contains("U12")) // Different rules for different age groups
                .WithMessage("U12 teams must have 5-15 players");

            RuleFor(x => x.PlayerCount)
                .InclusiveBetween(8, 25)
                .When(x => !x.AgeGroup.Contains("U12"))
                .WithMessage("Teams must have 8-25 players");

            // Cross-property validation
            RuleFor(x => x)
                .Must(HaveValidCoachToPlayerRatio)
                .WithMessage("Coach to player ratio must be at least 1:8");

            // Conditional validation based on tournament type
            When(x => x.IsTournamentRegistration, () =>
            {
                RuleFor(x => x.InsuranceCertificate).NotEmpty();
                RuleFor(x => x.MedicalClearance).NotEmpty();
            });
        }

        private bool BeValidAgeGroup(string ageGroup)
        {
            var validGroups = new[] { "U8", "U10", "U12", "U14", "U16", "U18", "Adult" };
            return validGroups.Contains(ageGroup);
        }

        private bool HaveValidCoachToPlayerRatio(TeamRegistrationRequest request)
        {
            if (request.CoachCount == 0) return false;
            return (double)request.PlayerCount / request.CoachCount <= 8;
        }
    }

    // DTOs for the examples
    public record LoginRequest(string Username, string Password);

    public class TeamRegistrationRequest
    {
        public string TeamName { get; set; }
        public string AgeGroup { get; set; }
        public int PlayerCount { get; set; }
        public int CoachCount { get; set; }
        public bool IsTournamentRegistration { get; set; }
        public string InsuranceCertificate { get; set; }
        public string MedicalClearance { get; set; }
    }
}