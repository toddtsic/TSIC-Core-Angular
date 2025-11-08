namespace TSIC.API.Dtos;

public record FamilyUserSummaryDto(string FamilyUserId, string DisplayName, string UserName);

public record FamilyPlayerDto(
    string PlayerId,
    string FirstName,
    string LastName,
    string Gender,
    string? Dob,
    bool Registered
);

public record FamilyBootstrapResponse(
    string JobPath,
    FamilyUserSummaryDto FamilyUser,
    IEnumerable<FamilyPlayerDto> Players
);
