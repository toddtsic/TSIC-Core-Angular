using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

public record AdnCredentialsViewModel
{
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}

public record PaymentRequestDto
{
    [Required, JsonRequired]
    public required string JobPath { get; init; } = string.Empty;
    [Required, JsonRequired]
    public required PaymentOption PaymentOption { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
    // Set when the customer pays by eCheck (ACH bank-account debit). Mutually exclusive with CreditCard.
    public BankAccountInfo? BankAccount { get; init; }
    public string? IdempotencyKey { get; init; }
    // Independent VerticalInsure (RegSaver) policy info: populated only when insurance was purchased separately
    public bool? ViConfirmed { get; init; }
    public string? ViPolicyNumber { get; init; }
    public DateTime? ViPolicyCreateDate { get; init; }
    // Quote identifiers chosen client-side for independent insurance purchase. Transient only; never stored after payment.
    public List<string>? ViQuoteIds { get; init; }
    // Optional token issued by VerticalInsure frontend (vaulted payment method). Mutually exclusive with CreditCard data when invoking insurance purchase.
    public string? ViToken { get; init; }
}

public enum PaymentOption
{
    PIF, // Pay In Full
    Deposit,
    ARB // Automated Recurring Billing
}

public record CreditCardInfo
{
    public string? Number { get; init; }
    public string? Expiry { get; set; } // MMYY
    public string? Code { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Address { get; init; }
    public string? Zip { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; set; }
}

// eCheck.Net (ACH) customer-facing bank account info. Sanitized in-place by the validator
// (digits-only routing/account/phone). AccountType: "checking" | "savings" | "businessChecking".
public record BankAccountInfo
{
    public string? AccountType { get; init; }
    public string? RoutingNumber { get; set; }
    public string? AccountNumber { get; set; }
    public string? NameOnAccount { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Address { get; init; }
    public string? Zip { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; set; }
}

public record PaymentResponseDto
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    // Machine-readable error code for client handling (e.g., display, retry logic)
    public string? ErrorCode { get; init; }
    public string? TransactionId { get; init; }
    public string? SubscriptionId { get; init; }
    // When multiple registrations create distinct subscriptions, this map is populated instead of SubscriptionId.
    public Dictionary<Guid, string>? SubscriptionIds { get; init; }
    // Registrations that failed subscription creation in multi-subscription ARB flow.
    public List<Guid>? FailedSubscriptionIds { get; init; }
    // Indicates that at least one subscription succeeded while others failed.
    public bool PartialSuccess => SubscriptionIds != null && SubscriptionIds.Count > 0 && FailedSubscriptionIds != null && FailedSubscriptionIds.Count > 0;
}

// One row in the canonical-CC-charge input: which registration to charge, how much.
public sealed record RegistrationChargeItem
{
    public required Guid RegistrationId { get; init; }
    public required decimal Amount { get; init; }
}

// Per-registration outcome from ChargeRegistrationsCcAsync. Each registration is
// charged independently, so a batch can be mixed: a captured player is Success=true
// with the applied amount, a declined player is Success=false with the decline Error.
public sealed record RegistrationCcChargeOutcome
{
    public required Guid RegistrationId { get; init; }
    public required bool Success { get; init; }
    public decimal? ChargedAmount { get; init; }
    public string? Error { get; init; }
}

// Canonical result from the per-player CC charge engine. ONE ADN transaction PER
// registration (parent self-pay sends N regs → N charges; admin sends 1). Outcomes
// mirror the input order; each captured player's own transaction id lives on its RA
// row. Result.TransactionId is the first successful charge (informational only);
// Success is true only when EVERY registration captured.
public sealed record RegistrationCcChargeResult
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? TransactionId { get; init; }
    public string? InvoiceNumber { get; init; }
    public required IReadOnlyList<RegistrationCcChargeOutcome> Outcomes { get; init; }
}
