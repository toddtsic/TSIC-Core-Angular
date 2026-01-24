using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

public class AdnCredentialsViewModel
{
    public string? AdnLoginId { get; set; }
    public string? AdnTransactionKey { get; set; }
}

public class PaymentRequestDto
{
    [Required, JsonRequired]
    public string JobPath { get; set; } = string.Empty;
    [Required, JsonRequired]
    public PaymentOption PaymentOption { get; set; }
    public CreditCardInfo? CreditCard { get; set; }
    public string? IdempotencyKey { get; set; }
    // Independent VerticalInsure (RegSaver) policy info: populated only when insurance was purchased separately
    public bool? ViConfirmed { get; set; }
    public string? ViPolicyNumber { get; set; }
    public DateTime? ViPolicyCreateDate { get; set; }
    // Quote identifiers chosen client-side for independent insurance purchase. Transient only; never stored after payment.
    public List<string>? ViQuoteIds { get; set; }
    // Optional token issued by VerticalInsure frontend (vaulted payment method). Mutually exclusive with CreditCard data when invoking insurance purchase.
    public string? ViToken { get; set; }
}

public enum PaymentOption
{
    PIF, // Pay In Full
    Deposit,
    ARB // Automated Recurring Billing
}

public class CreditCardInfo
{
    public string? Number { get; set; }
    public string? Expiry { get; set; } // MMYY
    public string? Code { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Address { get; set; }
    public string? Zip { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class PaymentResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    // Machine-readable error code for client handling (e.g., display, retry logic)
    public string? ErrorCode { get; set; }
    public string? TransactionId { get; set; }
    public string? SubscriptionId { get; set; }
    // When multiple registrations create distinct subscriptions, this map is populated instead of SubscriptionId.
    public Dictionary<Guid, string>? SubscriptionIds { get; set; }
    // Registrations that failed subscription creation in multi-subscription ARB flow.
    public List<Guid>? FailedSubscriptionIds { get; set; }
    // Indicates that at least one subscription succeeded while others failed.
    public bool PartialSuccess => SubscriptionIds != null && SubscriptionIds.Count > 0 && FailedSubscriptionIds != null && FailedSubscriptionIds.Count > 0;
}
