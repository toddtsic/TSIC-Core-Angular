using FluentValidation;

namespace TSIC.API.Dtos;

public sealed record ClubRepClubDto
{
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
}

public sealed record AddClubToRepRequest
{
    public required string ClubName { get; init; }
}

public class AddClubToRepRequestValidator : AbstractValidator<AddClubToRepRequest>
{
    public AddClubToRepRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");
    }
}

public sealed record RemoveClubFromRepRequest
{
    public required string ClubName { get; init; }
}

public class RemoveClubFromRepRequestValidator : AbstractValidator<RemoveClubFromRepRequest>
{
    public RemoveClubFromRepRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required");
    }
}

public sealed record TeamsMetadataResponse
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required List<ClubTeamDto> AvailableClubTeams { get; init; }
    public required List<RegisteredTeamDto> RegisteredTeams { get; init; }
    public required List<AgeGroupDto> AgeGroups { get; init; }
}

public sealed record ClubTeamDto
{
    public required int ClubTeamId { get; init; }
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string ClubTeamLevelOfPlay { get; init; }
}

public sealed record RegisteredTeamDto
{
    public required Guid TeamId { get; init; }
    public required int ClubTeamId { get; init; }
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string ClubTeamLevelOfPlay { get; init; }
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
}

public sealed record AgeGroupDto
{
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    public required int MaxTeams { get; init; }
    public required int RegisteredCount { get; init; }
    public required decimal RosterFee { get; init; }
    public required decimal TeamFee { get; init; }
}

public sealed record RegisterTeamRequest
{
    public required int ClubTeamId { get; init; }
    public required string JobPath { get; init; }
    public Guid? AgeGroupId { get; init; }
}

public class RegisterTeamRequestValidator : AbstractValidator<RegisterTeamRequest>
{
    public RegisterTeamRequestValidator()
    {
        RuleFor(x => x.ClubTeamId)
            .GreaterThan(0).WithMessage("ClubTeamId must be greater than 0");

        RuleFor(x => x.JobPath)
            .NotEmpty().WithMessage("JobPath is required")
            .MaximumLength(100).WithMessage("JobPath cannot exceed 100 characters");
    }
}

public sealed record RegisterTeamResponse
{
    public required Guid TeamId { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
}

public sealed record AddClubTeamRequest
{
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string ClubTeamLevelOfPlay { get; init; }
}

public class AddClubTeamRequestValidator : AbstractValidator<AddClubTeamRequest>
{
    public AddClubTeamRequestValidator()
    {
        RuleFor(x => x.ClubTeamName)
            .NotEmpty().WithMessage("Team name is required")
            .MaximumLength(200).WithMessage("Team name cannot exceed 200 characters");

        RuleFor(x => x.ClubTeamGradYear)
            .NotEmpty().WithMessage("Graduation year is required")
            .Matches(@"^\d{4}$").WithMessage("Graduation year must be a 4-digit year");

        RuleFor(x => x.ClubTeamLevelOfPlay)
            .NotEmpty().WithMessage("Level of play is required")
            .MaximumLength(50).WithMessage("Level of play cannot exceed 50 characters");
    }
}

public sealed record AddClubTeamResponse
{
    public required int ClubTeamId { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
}

public sealed record AddClubToRepResponse
{
    public required bool Success { get; init; }
    public required string ClubName { get; init; }
    public List<ClubSearchResult>? SimilarClubs { get; init; }
    public string? Message { get; init; }
}

public sealed record ValidateClubRepRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string JobPath { get; init; }
}

public sealed record ValidateClubRepResponse
{
    public required bool IsValid { get; init; }
    public required string? ClubName { get; init; }
    public string? Message { get; init; }
}
