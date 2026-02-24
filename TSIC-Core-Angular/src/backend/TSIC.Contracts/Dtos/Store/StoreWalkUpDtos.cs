namespace TSIC.Contracts.Dtos.Store;

public record StoreWalkUpRegisterRequest
{
	public required string JobPath { get; init; }
	public required string FirstName { get; init; }
	public required string LastName { get; init; }
	public required string Email { get; init; }
	public required string Phone { get; init; }
	public required string StreetAddress { get; init; }
	public required string City { get; init; }
	public required string State { get; init; }
	public required string Zip { get; init; }
}

public record StoreWalkUpRegisterResponse
{
	public required string AccessToken { get; init; }
	public required string RefreshToken { get; init; }
	public required int ExpiresIn { get; init; }
}
