using AuthorizeNet.Api.Contracts.V1;

namespace TSIC.API.Dtos;

// Transport shapes for Authorize.Net gateway interactions.
// Intent: Keep immutable request/response input records separate from service implementation.
// These are consumed by IAdnApiService and PaymentService.

public record AdnArbCreateRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string CardNumber,
    string CardCode,
    string Expiry,
    string FirstName,
    string LastName,
    string Address,
    string Zip,
    string Email,
    string Phone,
    string InvoiceNumber,
    string Description,
    decimal PerIntervalCharge,
    DateTime? StartDate,
    short BillingOccurrences,
    short IntervalLength
);

public record AdnAuthorizeRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string CardNumber,
    string CardCode,
    string Expiry,
    string FirstName,
    string LastName,
    string Address,
    string Zip,
    decimal Amount
);

public record AdnChargeRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string CardNumber,
    string CardCode,
    string Expiry,
    string FirstName,
    string LastName,
    string Address,
    string Zip,
    string Email,
    string Phone,
    decimal Amount,
    string InvoiceNumber,
    string Description
);

public record AdnRefundRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string CardNumberLast4,
    string Expiry,
    string TransactionId,
    decimal Amount,
    string InvoiceNumber
);

public record AdnVoidRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string TransactionId
);

public record AdnArbUpdateRequest(
    AuthorizeNet.Environment Env,
    string LoginId,
    string TransactionKey,
    string SubscriptionId,
    decimal ChargePerOccurrence,
    string CardNumber,
    string ExpirationDate,
    string CardCode,
    string FirstName,
    string LastName,
    string Address,
    string Zip,
    string Email
);

public record AdnArbCreateResult(
    bool Success,
    string? SubscriptionId,
    string? TransactionId,
    string? AuthCode,
    string? ResponseCode,
    string? RawGatewayCode,
    string MessageForUser,
    string GatewayMessage,
    string? CardLast4
);
