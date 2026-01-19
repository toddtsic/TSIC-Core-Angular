using FluentValidation;

namespace TSIC.Contracts.Dtos;

public sealed record ClubRepClubDto
{
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
}

public sealed record InitializeRegistrationRequest
{
    public required string ClubName { get; init; }
    public required string JobPath { get; init; }
}

public class InitializeRegistrationRequestValidator : AbstractValidator<InitializeRegistrationRequest>
{
    public InitializeRegistrationRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required");

        RuleFor(x => x.JobPath)
            .NotEmpty().WithMessage("Job path is required");
    }
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

public sealed record UpdateClubNameRequest
{
    public required string OldClubName { get; init; }
    public required string NewClubName { get; init; }
}

public class UpdateClubNameRequestValidator : AbstractValidator<UpdateClubNameRequest>
{
    public UpdateClubNameRequestValidator()
    {
        RuleFor(x => x.OldClubName)
            .NotEmpty().WithMessage("Old club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");

        RuleFor(x => x.NewClubName)
            .NotEmpty().WithMessage("New club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");
    }
}

public sealed record TeamsMetadataResponse
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required List<SuggestedTeamNameDto> SuggestedTeamNames { get; init; }
    public required List<RegisteredTeamDto> RegisteredTeams { get; init; }
    public required List<AgeGroupDto> AgeGroups { get; init; }
    public required bool BPayBalanceDue { get; init; }
    public required bool BTeamsFullPaymentRequired { get; init; }
    public string? PlayerRegRefundPolicy { get; init; }
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required bool BApplyProcessingFeesToTeamDeposit { get; init; }
    public UserContactInfoDto? ClubRepContactInfo { get; init; }
}

public sealed record SuggestedTeamNameDto
{
    public required string TeamName { get; init; }
    public required int UsageCount { get; init; }
    public required int Year { get; init; }
}

public sealed record RegisteredTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    public required string? LevelOfPlay { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal DepositDue { get; init; }
    public required decimal AdditionalDue { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public required bool BWaiverSigned3 { get; init; }
    public required decimal CcOwedTotal { get; init; }
    public required decimal CkOwedTotal { get; init; }
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
    public required string TeamName { get; init; }
    public required Guid AgeGroupId { get; init; }
    public string? LevelOfPlay { get; init; }
}

public class RegisterTeamRequestValidator : AbstractValidator<RegisterTeamRequest>
{
    public RegisterTeamRequestValidator()
    {
        RuleFor(x => x.TeamName)
            .NotEmpty().WithMessage("Team name is required")
            .MaximumLength(100).WithMessage("Team name cannot exceed 100 characters");

        RuleFor(x => x.AgeGroupId)
            .NotEmpty().WithMessage("Age group is required");
    }
}

public sealed record RegisterTeamResponse
{
    public required Guid TeamId { get; init; }
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
public sealed record CheckExistingRegistrationsResponse
{
    public required bool HasConflict { get; init; }
    public string? OtherRepUsername { get; init; }
    public int TeamCount { get; init; }
}

public sealed record RecalculateTeamFeesRequest
{
    public Guid? JobId { get; init; }
    public Guid? TeamId { get; init; }
}

public class RecalculateTeamFeesRequestValidator : AbstractValidator<RecalculateTeamFeesRequest>
{
    public RecalculateTeamFeesRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.JobId.HasValue && !x.TeamId.HasValue) || (!x.JobId.HasValue && x.TeamId.HasValue))
            .WithMessage("Exactly one of JobId or TeamId must be provided");
    }
}

public sealed record RecalculateTeamFeesResponse
{
    public required int UpdatedCount { get; init; }
    public required List<TeamFeeUpdateDto> Updates { get; init; }
    public required int SkippedCount { get; init; }
    public required List<string> SkippedReasons { get; init; }
}

public sealed record TeamFeeUpdateDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgeGroupName { get; init; }
    public required decimal OldFeeBase { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal OldFeeProcessing { get; init; }
    public required decimal NewFeeProcessing { get; init; }
    public required string UpdatedBy { get; init; }
    public required DateTime UpdatedAt { get; init; }
}