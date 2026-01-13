using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FluentValidation;
using TSIC.Contracts.Dtos.VerticalInsure;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request payload for club rep team registration payment.
/// </summary>
public sealed record TeamPaymentRequestDto
{
    public required string JobPath { get; init; }
    public required Guid ClubRepRegId { get; init; }
    public required List<Guid> TeamIds { get; init; }
    public required decimal TotalAmount { get; init; }
    public required CreditCardInfo CreditCard { get; init; }
    public bool IncludesInsurance { get; init; }
}

public class TeamPaymentRequestDtoValidator : AbstractValidator<TeamPaymentRequestDto>
{
    public TeamPaymentRequestDtoValidator()
    {
        RuleFor(x => x.JobPath)
            .NotEmpty().WithMessage("JobPath is required");

        RuleFor(x => x.ClubRepRegId)
            .NotEmpty().WithMessage("Club rep registration ID is required");

        RuleFor(x => x.TeamIds)
            .NotEmpty().WithMessage("At least one team is required for payment");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("Payment amount must be greater than zero");

        RuleFor(x => x.CreditCard)
            .NotNull().WithMessage("Credit card information is required");
    }
}

/// <summary>
/// Response from team payment processing.
/// </summary>
public sealed record TeamPaymentResponseDto
{
    public required bool Success { get; init; }
    public string? TransactionId { get; init; }
    public string? ErrorMessage { get; init; }
    public required List<TeamPaymentDetail> TeamPayments { get; init; }
}

/// <summary>
/// Details of payment applied to a specific team.
/// </summary>
public sealed record TeamPaymentDetail
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required decimal AmountPaid { get; init; }
    public required decimal RemainingBalance { get; init; }
}

/// <summary>
/// Request to fetch pre-submit insurance offer for teams.
/// </summary>
public sealed record PreSubmitTeamInsuranceRequestDto
{
    public required Guid JobId { get; init; }
    public required Guid ClubRepRegId { get; init; }
}

/// <summary>
/// Pre-submit insurance offer response for team registration insurance.
/// </summary>
public sealed record PreSubmitTeamInsuranceDto
{
    public required bool Available { get; init; }
    public VITeamObjectResponse? TeamObject { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public string? StateId { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Request payload for team insurance purchase through VerticalInsure.
/// </summary>
public sealed record TeamInsurancePurchaseRequestDto
{
    [Required, JsonRequired]
    public required Guid JobId { get; init; }
    [Required, JsonRequired]
    public required Guid ClubRepRegId { get; init; }
    [Required, JsonRequired]
    public required List<Guid> TeamIds { get; init; }
    [Required, JsonRequired]
    public required List<string> QuoteIds { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
}

public class TeamInsurancePurchaseRequestDtoValidator : AbstractValidator<TeamInsurancePurchaseRequestDto>
{
    public TeamInsurancePurchaseRequestDtoValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty().WithMessage("Job ID is required");

        RuleFor(x => x.ClubRepRegId)
            .NotEmpty().WithMessage("Club rep registration ID is required");

        RuleFor(x => x.TeamIds)
            .NotEmpty().WithMessage("At least one team is required")
            .Must((dto, teamIds) => teamIds.Count == dto.QuoteIds.Count)
            .WithMessage("Number of teams must match number of quotes");

        RuleFor(x => x.QuoteIds)
            .NotEmpty().WithMessage("At least one quote ID is required");
    }
}

/// <summary>
/// Response from team insurance purchase.
/// </summary>
public sealed record TeamInsurancePurchaseResponseDto
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<Guid, string> Policies { get; init; } = new();
}

/// <summary>
/// Result structure for team insurance purchase operations.
/// </summary>
public sealed record VerticalInsureTeamPurchaseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<Guid, string> Policies { get; set; } = new();
}
