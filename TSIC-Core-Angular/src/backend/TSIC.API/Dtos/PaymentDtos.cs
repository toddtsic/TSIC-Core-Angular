namespace TSIC.API.DTOs;

public class AdnCredentialsViewModel
{
    public string? AdnLoginId { get; set; }
    public string? AdnTransactionKey { get; set; }
}

public class PaymentRequestDto
{
    public Guid JobId { get; set; }
    public Guid FamilyUserId { get; set; }
    public PaymentOption PaymentOption { get; set; }
    public CreditCardInfo? CreditCard { get; set; }
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
}

public class PaymentResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? TransactionId { get; set; }
    public string? SubscriptionId { get; set; }
}