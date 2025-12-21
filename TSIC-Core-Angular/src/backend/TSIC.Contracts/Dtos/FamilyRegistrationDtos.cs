namespace TSIC.Contracts.Dtos;

public record FamilyRegistrationRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required PersonDto Primary { get; init; }
    public required PersonDto Secondary { get; init; }
    public required AddressDto Address { get; init; }
    public required List<ChildDto> Children { get; init; }
}

public record PersonDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Cellphone { get; init; }
    public required string Email { get; init; }
}

public record AddressDto
{
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }
}

public record ChildDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Gender { get; init; }
    public string? Dob { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

public record FamilyRegistrationResponse
{
    public required bool Success { get; init; }
    public string? FamilyUserId { get; init; }
    public Guid? FamilyId { get; init; }
    public string? Message { get; init; }
}

public record FamilyUpdateRequest
{
    public required string Username { get; init; }
    public required PersonDto Primary { get; init; }
    public required PersonDto Secondary { get; init; }
    public required AddressDto Address { get; init; }
    public required List<ChildDto> Children { get; init; }
}

public record FamilyProfileResponse
{
    public required string Username { get; init; }
    public required PersonDto Primary { get; init; }
    public required PersonDto Secondary { get; init; }
    public required AddressDto Address { get; init; }
    public required List<ChildDto> Children { get; init; }
}
