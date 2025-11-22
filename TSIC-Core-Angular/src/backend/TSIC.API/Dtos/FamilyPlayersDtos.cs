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
    string? AdnSubscriptionId,
    string? AdnSubscriptionStatus,
    IReadOnlyDictionary<string, JsonElement> FormFieldValues
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
    RegSaverDetailsDto? RegSaverDetails = null,
    JobRegFormDto? JobRegForm = null,
    CcInfoDto? CcInfo = null,
    bool JobHasActiveDiscountCodes = false,
    bool JobUsesAmex = false
);

public record CcInfoDto(
    string? FirstName,
    string? LastName,
    string? StreetAddress,
    string? Zip,
    string? Email,
    string? Phone
);

// Typed field definition combined with the current value for a specific registration
public record RegistrationFormFieldDto(
    string Name,
    string DbColumn,
    string DisplayName,
    string InputType,
    string? DataSource,
    List<ProfileFieldOption>? Options,
    FieldValidation? Validation,
    int Order,
    string Visibility,
    bool Computed,
    FieldCondition? ConditionalOn,
    JsonElement? Value
);

// Minimal name/value projection for visible (non-admin, non-hidden) fields
public record RegistrationFieldValueDto(
    string Name,
    JsonElement? Value
);

// Immutable job-level schema (shared across players and registrations)
public record JobRegFormDto(
    string Version,
    string? CoreProfileName,
    IReadOnlyList<JobRegFieldDto> Fields,
    IReadOnlyList<string> WaiverFieldNames,
    string? ConstraintType
);

public record JobRegFieldDto(
    string Name,
    string DbColumn,
    string DisplayName,
    string InputType,
    string? DataSource,
    IReadOnlyList<ProfileFieldOption>? Options,
    FieldValidation? Validation,
    int Order,
    string Visibility,
    bool Computed,
    FieldCondition? ConditionalOn
);
