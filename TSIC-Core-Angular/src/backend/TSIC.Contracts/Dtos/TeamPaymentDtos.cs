using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FluentValidation;
using TSIC.Contracts.Dtos.VerticalInsure;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request payload for club rep team registration payment.
/// Context (jobId, registration) derived from regId token claim.
/// </summary>
public sealed record TeamPaymentRequestDto
{
    public required List<Guid> TeamIds { get; init; }
    public required decimal TotalAmount { get; init; }
    public required CreditCardInfo CreditCard { get; init; }
}

public class TeamPaymentRequestDtoValidator : AbstractValidator<TeamPaymentRequestDto>
{
    public TeamPaymentRequestDtoValidator()
    {
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
    public string? Error { get; init; }
    public string? Message { get; init; }
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
    public required List<Guid> TeamIds { get; init; }
    [Required, JsonRequired]
    public required List<string> QuoteIds { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
}

public class TeamInsurancePurchaseRequestDtoValidator : AbstractValidator<TeamInsurancePurchaseRequestDto>
{
    public TeamInsurancePurchaseRequestDtoValidator()
    {
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
    public required bool Success { get; init; }
    public required string? Error { get; init; }
    public required Dictionary<Guid, string> Policies { get; init; } = new();
}
