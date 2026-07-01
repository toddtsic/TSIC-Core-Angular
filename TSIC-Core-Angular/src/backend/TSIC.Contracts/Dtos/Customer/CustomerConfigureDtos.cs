namespace TSIC.Contracts.Dtos.Customer;

// ── Response DTOs ────────────────────────────────────────

public sealed record CustomerListDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required bool BAllowAmex { get; init; }
    public required int JobCount { get; init; }

    /// <summary>Name of the job with the most recent registration activity, or null if none.</summary>
    public required string? LastActiveJobName { get; init; }

    /// <summary>Timestamp of the customer's most recent registration activity, or null if none.</summary>
    public required DateTime? LastActiveJobDate { get; init; }
}

public sealed record CustomerDetailDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required bool BAllowAmex { get; init; }
    public required string? AdnLoginId { get; init; }
    public required string? AdnTransactionKey { get; init; }
}

// ── Request DTOs ─────────────────────────────────────────

public sealed record CreateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required bool BAllowAmex { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}

public sealed record UpdateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required bool BAllowAmex { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}
