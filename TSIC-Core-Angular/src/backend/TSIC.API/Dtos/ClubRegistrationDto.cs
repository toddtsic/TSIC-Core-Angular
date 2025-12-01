namespace TSIC.API.Dtos;

public record ClubRegistrationRequest(
    string ClubName,
    string FirstName,
    string LastName,
    string Email,
    string Username,
    string Password,
    string StreetAddress,
    string City,
    string State,
    string PostalCode,
    string Cellphone
);

public record ClubRegistrationResponse(
    bool Success,
    int? ClubId,
    string? UserId,
    string? Message,
    List<ClubSearchResult>? SimilarClubs = null
);

public record ClubSearchResult(
    int ClubId,
    string ClubName,
    string? State,
    int TeamCount,
    int MatchScore
);
