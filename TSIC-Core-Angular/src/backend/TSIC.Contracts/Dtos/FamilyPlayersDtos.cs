using System.Text.Json;

namespace TSIC.Contracts.Dtos;

public record FamilyUserSummaryDto
{
    public required string FamilyUserId { get; init; }
    public required string DisplayName { get; init; }
    public required string UserName { get; init; }
}

// Financial summary for a registration (money semantics -> decimal)
public record RegistrationFinancialsDto
{
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeDonation { get; init; }
    public required decimal FeeLateFee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal PaidTotal { get; init; }
}

// Minimal prior registration information for a player within the current job
public record FamilyPlayerRegistrationDto
{
    public required Guid RegistrationId { get; init; }
    public required bool Active { get; init; }
    public required RegistrationFinancialsDto Financials { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public string? AssignedTeamName { get; init; }
    public string? AdnSubscriptionId { get; init; }
    public string? AdnSubscriptionStatus { get; init; }
    public decimal? AdnSubscriptionAmountPerOccurence { get; init; }
    public short? AdnSubscriptionBillingOccurences { get; init; }
    public short? AdnSubscriptionIntervalLength { get; init; }
    public DateTime? AdnSubscriptionStartDate { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> FormFieldValues { get; init; }
}

public record FamilyPlayerDto
{
    public required string PlayerId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Gender { get; init; }
    public string? Dob { get; init; }
    public required bool Registered { get; init; }
    public required bool Selected { get; init; }
    public required IReadOnlyList<FamilyPlayerRegistrationDto> PriorRegistrations { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? DefaultFieldValues { get; init; }
}

public record RegSaverDetailsDto
{
    public required string PolicyNumber { get; init; }
    public required DateTime PolicyCreateDate { get; init; }
}

public record FamilyPlayersResponseDto
{
    public required FamilyUserSummaryDto FamilyUser { get; init; }
    public required IEnumerable<FamilyPlayerDto> FamilyPlayers { get; init; }
    public RegSaverDetailsDto? RegSaverDetails { get; init; }
    public JobRegFormDto? JobRegForm { get; init; }
    public CcInfoDto? CcInfo { get; init; }
    public required bool JobHasActiveDiscountCodes { get; init; }
    public required bool JobUsesAmex { get; init; }
}

public record CcInfoDto
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? StreetAddress { get; init; }
    public string? Zip { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

// Typed field definition combined with the current value for a specific registration
public record RegistrationFormFieldDto
{
    public required string Name { get; init; }
    public required string DbColumn { get; init; }
    public required string DisplayName { get; init; }
    public required string InputType { get; init; }
    public string? DataSource { get; init; }
    public List<ProfileFieldOption>? Options { get; init; }
    public FieldValidation? Validation { get; init; }
    public required int Order { get; init; }
    public required string Visibility { get; init; }
    public required bool Computed { get; init; }
    public FieldCondition? ConditionalOn { get; init; }
    public JsonElement? Value { get; init; }
}

// Minimal name/value projection for visible (non-admin, non-hidden) fields
public record RegistrationFieldValueDto
{
    public required string Name { get; init; }
    public JsonElement? Value { get; init; }
}

// Immutable job-level schema (shared across players and registrations)
public record JobRegFormDto
{
    public required string Version { get; init; }
    public string? CoreProfileName { get; init; }
    public required IReadOnlyList<JobRegFieldDto> Fields { get; init; }
    public required IReadOnlyList<string> WaiverFieldNames { get; init; }
    public string? ConstraintType { get; init; }
}

public record JobRegFieldDto
{
    public required string Name { get; init; }
    public required string DbColumn { get; init; }
    public required string DisplayName { get; init; }
    public required string InputType { get; init; }
    public string? DataSource { get; init; }
    public IReadOnlyList<ProfileFieldOption>? Options { get; init; }
    public FieldValidation? Validation { get; init; }
    public required int Order { get; init; }
    public required string Visibility { get; init; }
    public required bool Computed { get; init; }
    public FieldCondition? ConditionalOn { get; init; }
}
