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

public record FamilyPlayersResponseDto(
    FamilyUserSummaryDto FamilyUser,
    IEnumerable<FamilyPlayerDto> Players
);

// NOTE: Formerly used by GET /api/family/bootstrap (now removed). Retain DTOs for potential future consolidation
// into a unified /api/family/context endpoint. Remove if not referenced after context API is implemented.
public record FamilyBootstrapResponse(
    string JobPath,
    FamilyUserSummaryDto FamilyUser,
    IEnumerable<FamilyPlayerDto> Players
);
