namespace TSIC.API.Dtos;

public record FamilyRegistrationRequest(
    string Username,
    string Password,
    PersonDto Primary,
    PersonDto Secondary,
    AddressDto Address,
    List<ChildDto> Children
);

public record PersonDto(
    string FirstName,
    string LastName,
    string Cellphone,
    string Email
);

public record AddressDto(
    string StreetAddress,
    string City,
    string State,
    string PostalCode
);

public record ChildDto(
    string FirstName,
    string LastName,
    string Gender,
    string? Dob,
    string? Email,
    string? Phone
);

public record FamilyRegistrationResponse(
    bool Success,
    string? FamilyUserId,
    Guid? FamilyId,
    string? Message
);

public record FamilyUpdateRequest(
    string Username,
    PersonDto Primary,
    PersonDto Secondary,
    AddressDto Address,
    List<ChildDto> Children
);
