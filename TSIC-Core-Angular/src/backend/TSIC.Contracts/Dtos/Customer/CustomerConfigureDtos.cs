namespace TSIC.Contracts.Dtos.Customer;

// ── Response DTOs ────────────────────────────────────────

public sealed record CustomerListDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required int TzId { get; init; }
    public required string? TimezoneName { get; init; }
    public required int JobCount { get; init; }
}

public sealed record CustomerDetailDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required int TzId { get; init; }
    public required string? AdnLoginId { get; init; }
    public required string? AdnTransactionKey { get; init; }
}

public sealed record TimezoneDto
{
    public required int TzId { get; init; }
    public required string? TzName { get; init; }
}

// ── Request DTOs ─────────────────────────────────────────

public sealed record CreateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required int TzId { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}

public sealed record UpdateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required int TzId { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}
