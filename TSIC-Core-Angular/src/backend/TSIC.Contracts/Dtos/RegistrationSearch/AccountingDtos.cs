using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Accounting record for display in the detail panel.
/// </summary>
public record AccountingRecordDto
{
    public required int AId { get; init; }
    public required DateTime? Date { get; init; }
    public required string PaymentMethod { get; init; }
    public required decimal? DueAmount { get; init; }
    public required decimal? PaidAmount { get; init; }
    public string? Comment { get; init; }
    public string? CheckNo { get; init; }
    public string? PromoCode { get; init; }
    public bool? Active { get; init; }

    // CC details (for refund eligibility)
    public string? AdnTransactionId { get; init; }
    public string? AdnCc4 { get; init; }
    public string? AdnCcExpDate { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public bool CanRefund { get; init; }
}

/// <summary>
/// Request to create a new accounting record (manual payment entry).
/// </summary>
public record CreateAccountingRecordRequest
{
    public required Guid RegistrationId { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public decimal? DueAmount { get; init; }
    public decimal? PaidAmount { get; init; }
    public string? Comment { get; init; }
    public string? CheckNo { get; init; }
    public string? PromoCode { get; init; }
}

/// <summary>
/// Refund request — processes via Authorize.Net gateway.
/// </summary>
public record RefundRequest
{
    public required int AccountingRecordId { get; init; }
    public required decimal RefundAmount { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Refund result from Authorize.Net gateway.
/// </summary>
public record RefundResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? TransactionId { get; init; }
    public decimal? RefundedAmount { get; init; }
}

/// <summary>
/// Payment method option for the create-accounting dropdown.
/// </summary>
public record PaymentMethodOptionDto
{
    public required Guid PaymentMethodId { get; init; }
    public required string PaymentMethod { get; init; }
}

/// <summary>
/// Request to charge a credit card from the admin panel.
/// </summary>
public record RegistrationCcChargeRequest
{
    public required Guid RegistrationId { get; init; }
    public required CreditCardInfo CreditCard { get; init; }
    public required decimal Amount { get; init; }
}

/// <summary>
/// Response from an admin CC charge.
/// </summary>
public record RegistrationCcChargeResponse
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? TransactionId { get; init; }
    public decimal? ChargedAmount { get; init; }
}

/// <summary>
/// Request to record a check payment or correction/scholarship.
/// </summary>
public record RegistrationCheckOrCorrectionRequest
{
    public required Guid RegistrationId { get; init; }
    public required decimal Amount { get; init; }
    public required string PaymentType { get; init; }  // "Check" or "Correction"
    public string? CheckNo { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// Response from recording a check or correction.
/// </summary>
public record RegistrationCheckOrCorrectionResponse
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Request to edit an existing accounting record (comment and check# only).
/// </summary>
public record EditAccountingRecordRequest
{
    public string? Comment { get; init; }
    public string? CheckNo { get; init; }
}

/// <summary>
/// ARB subscription detail from Authorize.Net.
/// </summary>
public record SubscriptionDetailDto
{
    public required string SubscriptionId { get; init; }
    public required string Status { get; init; }
    public required decimal PerOccurrenceAmount { get; init; }
    public required int TotalOccurrences { get; init; }
    public required decimal TotalAmount { get; init; }
    public required DateTime StartDate { get; init; }
    public required string IntervalLabel { get; init; }
}
