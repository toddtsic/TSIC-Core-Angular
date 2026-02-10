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
