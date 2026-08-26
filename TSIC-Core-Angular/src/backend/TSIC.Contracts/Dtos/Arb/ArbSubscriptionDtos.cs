namespace TSIC.Contracts.Dtos.Arb;

public record ArbSubscriptionInfoDto
{
    public required string SubscriptionId { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required decimal ChargePerOccurrence { get; init; }
    public required decimal BalanceDue { get; init; }
    public required string RegistrantName { get; init; }
    public required string JobName { get; init; }
    public required DateTime StartDate { get; init; }
    public required int TotalOccurrences { get; init; }
    public required int IntervalMonths { get; init; }
}

public record ArbUpdateCcRequest
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string ExpirationMonth { get; init; }
    public required string ExpirationYear { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }

    // NO amount field, deliberately. This request comes from a family-reachable, self-service page,
    // and the server charges a card off the back of it. The amount used to travel in this body and
    // was charged verbatim; ArbDefensiveService re-derives it from the registration instead.
    // Do not add it back.
}

public record ArbUpdateCcResultDto
{
    public required bool SubscriptionUpdated { get; init; }
    public required bool BalanceCharged { get; init; }
    public decimal AmountCharged { get; init; }
    public string? TransactionId { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Summary of a job-wide ARB status refresh: every registration in the job with a
/// subscription ID checked live against Authorize.Net, stale statuses written back.
/// </summary>
public record ArbRefreshStatusesResultDto
{
    /// <summary>Registrations with a subscription ID that were checked against ADN.</summary>
    public required int Checked { get; init; }

    /// <summary>Registrations whose stored status differed from ADN and were updated.</summary>
    public required int Updated { get; init; }

    /// <summary>Registrations whose ADN status lookup failed (non-Ok response or error).</summary>
    public required int Failed { get; init; }
}
