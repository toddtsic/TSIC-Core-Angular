namespace TSIC.Contracts.Dtos;

/// <summary>
/// User contact and demographic information returned with auth tokens
/// for roles that need prefill data (e.g., Club Rep for payment forms).
/// Conditionally included based on role type.
/// </summary>
public record UserContactInfoDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Cellphone { get; init; }
    public string? Phone { get; init; }
}
