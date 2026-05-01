using AuthorizeNet.Api.Contracts.V1;

namespace TSIC.Contracts.Dtos;

// Transport shapes for Authorize.Net gateway interactions.
// Intent: Keep immutable request/response input records separate from service implementation.
// These are consumed by IAdnApiService and PaymentService.

public record AdnArbCreateRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string Expiry { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
    public required decimal PerIntervalCharge { get; init; }
    public DateTime? StartDate { get; init; }
    public required short BillingOccurrences { get; init; }
    public required short IntervalLength { get; init; }
}

public record AdnAuthorizeRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string Expiry { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required decimal Amount { get; init; }
}

public record AdnChargeRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string Expiry { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required decimal Amount { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Charge a customer-provided bank account (eCheck.Net debit) via Authorize.Net.
/// Counterpart to <see cref="AdnChargeRequest"/> — same envelope, swap CC fields for bank account.
/// echeckType is fixed to "WEB" in the implementation (customer authorized via website).
/// </summary>
public record AdnChargeBankAccountRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }

    /// <summary>"checking" | "savings" | "businessChecking" (per ADN bankAccountTypeEnum).</summary>
    public required string AccountType { get; init; }
    /// <summary>9-digit ABA routing number.</summary>
    public required string RoutingNumber { get; init; }
    /// <summary>Up to 17 chars per ADN schema.</summary>
    public required string AccountNumber { get; init; }
    /// <summary>Up to 22 chars per ADN schema.</summary>
    public required string NameOnAccount { get; init; }

    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }

    public required decimal Amount { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
}

public record AdnRefundRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string CardNumberLast4 { get; init; }
    public required string Expiry { get; init; }
    public required string TransactionId { get; init; }
    public required decimal Amount { get; init; }
    public required string InvoiceNumber { get; init; }
}

public record AdnVoidRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string TransactionId { get; init; }
}

// ARB-Trial subscription create — CC path. Two ADN charges scheduled:
//   1. Trial occurrence (the deposit)  — billed on StartDate at TrialAmount
//   2. Post-trial occurrence (balance) — billed StartDate + IntervalLengthDays, at PerIntervalCharge
// Interval unit is fixed to days so we can express the gap between deposit
// and balance dates exactly.
public record AdnArbCreateTrialRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string Expiry { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
    /// <summary>The deposit amount billed on the trial occurrence.</summary>
    public required decimal TrialAmount { get; init; }
    /// <summary>The balance amount billed on each post-trial occurrence (only one in our pattern).</summary>
    public required decimal PerIntervalCharge { get; init; }
    /// <summary>First charge date (deposit). Required — caller computes today + 1.</summary>
    public required DateTime StartDate { get; init; }
    /// <summary>Number of days from StartDate to the balance billing date.</summary>
    public required short IntervalLengthDays { get; init; }
}

// ARB-Trial subscription create — eCheck (ACH bankAccount) path.
// Mirror of AdnArbCreateTrialRequest with bankAccount fields swapped in
// for credit card fields.
public record AdnArbCreateTrialBankAccountRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }

    /// <summary>"checking" | "savings" | "businessChecking" (per ADN bankAccountTypeEnum).</summary>
    public required string AccountType { get; init; }
    /// <summary>9-digit ABA routing number.</summary>
    public required string RoutingNumber { get; init; }
    /// <summary>Up to 17 chars per ADN schema.</summary>
    public required string AccountNumber { get; init; }
    /// <summary>Up to 22 chars per ADN schema.</summary>
    public required string NameOnAccount { get; init; }

    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
    public required decimal TrialAmount { get; init; }
    public required decimal PerIntervalCharge { get; init; }
    public required DateTime StartDate { get; init; }
    public required short IntervalLengthDays { get; init; }
}

public record AdnArbUpdateRequest
{
    public required AuthorizeNet.Environment Env { get; init; }
    public required string LoginId { get; init; }
    public required string TransactionKey { get; init; }
    public required string SubscriptionId { get; init; }
    public required decimal ChargePerOccurrence { get; init; }
    public required string CardNumber { get; init; }
    public required string ExpirationDate { get; init; }
    public required string CardCode { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
}

public record AdnArbCreateResult
{
    public required bool Success { get; init; }
    public string? SubscriptionId { get; init; }
    public string? TransactionId { get; init; }
    public string? AuthCode { get; init; }
    public string? ResponseCode { get; init; }
    public string? RawGatewayCode { get; init; }
    public required string MessageForUser { get; init; }
    public required string GatewayMessage { get; init; }
    public string? CardLast4 { get; init; }
}

// Result of a $0.01 auth-then-void card validation. Used as a synchronous
// "is this card good?" check in front of operations whose actual charge is
// deferred (ARB subscription creation, ARB card update) — without it, a
// declined card wouldn't surface until the next-morning batch.
public record AdnPennyVerifyResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuthTransactionId { get; init; }
}
