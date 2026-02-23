namespace TSIC.Contracts.Dtos.Customer;

// ── Response DTOs ────────────────────────────────────────

public sealed record CustomerGroupDto
{
    public required int Id { get; init; }
    public required string CustomerGroupName { get; init; }
    public required int MemberCount { get; init; }
}

public sealed record CustomerGroupMemberDto
{
    public required int Id { get; init; }
    public required int CustomerGroupId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
}

public sealed record CustomerLookupDto
{
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
}

// ── Request DTOs ─────────────────────────────────────────

public sealed record CreateCustomerGroupRequest
{
    public required string CustomerGroupName { get; init; }
}

public sealed record RenameCustomerGroupRequest
{
    public required string CustomerGroupName { get; init; }
}

public sealed record AddCustomerGroupMemberRequest
{
    public required Guid CustomerId { get; init; }
}
