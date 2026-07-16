namespace TSIC.Contracts.Dtos.AdminExpiry;

/// <summary>A customer having at least one job whose admin door (ExpiryAdmin) has closed.</summary>
public record AdminExpiryCustomerDto
{
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required List<AdminExpiryJobDto> Jobs { get; init; }
}

public record AdminExpiryJobDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required DateTime ExpiryAdmin { get; init; }
}

public record UpdateAdminExpiryRequest
{
    public required DateTime ExpiryAdmin { get; init; }
}
