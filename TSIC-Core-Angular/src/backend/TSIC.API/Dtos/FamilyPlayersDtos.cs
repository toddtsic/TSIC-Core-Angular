using System.Text.Json;

namespace TSIC.API.Dtos;

public record FamilyUserSummaryDto(string FamilyUserId, string DisplayName, string UserName);

// Financial summary for a registration (money semantics -> decimal)
public record RegistrationFinancialsDto(
    decimal FeeBase,
    decimal FeeProcessing,
    decimal FeeDiscount,
    decimal FeeDonation,
    decimal FeeLateFee,
    decimal FeeTotal,
    decimal OwedTotal,
    decimal PaidTotal
);

// Minimal prior registration information for a player within the current job
public record FamilyPlayerRegistrationDto(
    Guid RegistrationId,
    bool Active,
    RegistrationFinancialsDto Financials,
    Guid? AssignedTeamId,
    string? AssignedTeamName,
    IReadOnlyDictionary<string, JsonElement> FormValues
);

public record FamilyPlayerDto(
    string PlayerId,
    string FirstName,
    string LastName,
    string Gender,
    string? Dob,
    bool Registered,
    bool Selected,
    IReadOnlyList<FamilyPlayerRegistrationDto> PriorRegistrations
);

public record RegSaverDetailsDto(
    string PolicyNumber,
    DateTime PolicyCreateDate
);

public record FamilyPlayersResponseDto(
    FamilyUserSummaryDto FamilyUser,
    IEnumerable<FamilyPlayerDto> FamilyPlayers,
    RegSaverDetailsDto? RegSaverDetails = null
);
