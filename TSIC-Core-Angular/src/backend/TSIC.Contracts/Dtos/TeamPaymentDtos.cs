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
/// eCheck (ACH) counterpart to <see cref="TeamPaymentRequestDto"/>. Same envelope,
/// swap the credit-card field for bank-account info.
/// </summary>
public sealed record TeamEcheckPaymentRequestDto
{
    public required List<Guid> TeamIds { get; init; }
    public required decimal TotalAmount { get; init; }
    public required BankAccountInfo BankAccount { get; init; }
}

public class TeamEcheckPaymentRequestDtoValidator : AbstractValidator<TeamEcheckPaymentRequestDto>
{
    public TeamEcheckPaymentRequestDtoValidator()
    {
        RuleFor(x => x.TeamIds).NotEmpty().WithMessage("At least one team is required for payment");
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("Payment amount must be greater than zero");
        RuleFor(x => x.BankAccount).NotNull().WithMessage("Bank account information is required");
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
/// Request payload for ARB-Trial team registration payment.
/// One ADN ARB subscription per team is created — deposit billed tomorrow,
/// balance billed on the job's configured AdnStartDateAfterTrial. Either
/// CreditCard or BankAccount must be supplied (not both). When the rep
/// registers after the configured balance date, the backend falls back to
/// a single full-amount charge (no ARB sub created).
/// </summary>
public sealed record TeamArbTrialPaymentRequestDto
{
    public required List<Guid> TeamIds { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
    public BankAccountInfo? BankAccount { get; init; }
}

public class TeamArbTrialPaymentRequestDtoValidator : AbstractValidator<TeamArbTrialPaymentRequestDto>
{
    public TeamArbTrialPaymentRequestDtoValidator()
    {
        RuleFor(x => x.TeamIds).NotEmpty().WithMessage("At least one team is required for payment");
        RuleFor(x => x).Must(x => x.CreditCard != null || x.BankAccount != null)
            .WithMessage("Either credit card or bank account information is required");
        RuleFor(x => x).Must(x => !(x.CreditCard != null && x.BankAccount != null))
            .WithMessage("Provide either credit card or bank account, not both");
    }
}

/// <summary>
/// Per-team result of an ARB-Trial submission.
/// </summary>
public sealed record TeamArbTrialResultDto
{
    public required Guid TeamId { get; init; }
    /// <summary>true = ADN sub created and stamped on the team; false = batch stopped here, this team did not register.</summary>
    public required bool Registered { get; init; }
    public string? AdnSubscriptionId { get; init; }
    public decimal? DepositCharge { get; init; }
    public decimal? BalanceCharge { get; init; }
    public DateTime? DepositDate { get; init; }
    public DateTime? BalanceDate { get; init; }
    /// <summary>Populated only on the team that stopped the batch.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Response from ARB-Trial team payment processing. Capture-what-you-can
/// semantics: the loop stops on the first failure but already-registered
/// teams are kept (ARB subs left active so money flows to the tournament).
/// </summary>
public sealed record TeamArbTrialPaymentResponseDto
{
    /// <summary>true if every team in the batch was registered; false if any team failed (including a partial-success outcome).</summary>
    public required bool Success { get; init; }
    /// <summary>"FALLBACK_FULL_CHARGE" when balance date is in the past — single CC charge replaces the trial flow.</summary>
    public string? Mode { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    /// <summary>Per-team results in the order they were submitted. The first non-Registered entry is the team that stopped the batch.</summary>
    public required List<TeamArbTrialResultDto> Teams { get; init; } = new();
    /// <summary>Team IDs that were not attempted because an earlier team in the batch failed.</summary>
    public required List<Guid> NotAttempted { get; init; } = new();
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
