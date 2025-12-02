using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace TSIC.API.Dtos.VerticalInsure;
#region viPlayer

public record RequestVIPlayerRequest
{
    [Required]
    public string JobName { get; init; } = string.Empty;
    [Required]
    public Guid JobId { get; init; }
    [Required]
    public string FamilyUserId { get; init; } = string.Empty;
}

public record RequestVITeamRequest
{
    [Required]
    public string JobName { get; init; } = string.Empty;
    [Required]
    public Guid JobId { get; init; }
    [Required]
    public Guid ClubRepRegId { get; init; }
}

public record VIPlayerObjectResponse
{
    [JsonPropertyName("client_id")] public string ClientId { get; init; } = string.Empty;
    [Required] public VIThemeDto Theme { get; init; } = new();
    [JsonPropertyName("product_config")] public VIPlayerProductConfigDto ProductConfig { get; init; } = new();
    public VIPaymentsDto Payments { get; init; } = new();
}

public record VIPlayerProductConfigDto
{
    [JsonPropertyName("registration-cancellation")]
    public List<VIPlayerProductDto> RegistrationCancellation { get; init; } = new();
}

public record VIPlayerProductDto
{
    public VICustomerDto Customer { get; init; } = new();
    public VIPlayerMetadataDto Metadata { get; init; } = new();
    [JsonPropertyName("policy_attributes")] public VIPlayerPolicyAttributes PolicyAttributes { get; init; } = new();
    [JsonPropertyName("offer_Id")] public string OfferId { get; init; } = string.Empty;
}

public record VIPlayerMetadataDto
{
    [JsonPropertyName("context_name")] public string ContextName { get; init; } = string.Empty;
    [JsonPropertyName("context_event")] public string ContextEvent { get; init; } = string.Empty;
    [JsonPropertyName("context_description")] public string ContextDescription { get; init; } = string.Empty;
    [JsonPropertyName("tsic_registrationid")] public Guid TsicRegistrationId { get; init; }
    [JsonPropertyName("tsic_secondchance")] public string TsicSecondChance { get; init; } = string.Empty;
    [JsonPropertyName("tsic_customer")] public string TsicCustomer { get; init; } = string.Empty;
}

public record VIPlayerPolicyAttributes
{
    [JsonPropertyName("event_start_date")] public DateOnly? EventStartDate { get; init; }
    [JsonPropertyName("event_end_date")] public DateOnly? EventEndDate { get; init; }
    [JsonPropertyName("insurable_amount")] public int InsurableAmount { get; init; }
    [JsonPropertyName("participant")] public VIParticipantDto Participant { get; init; } = new();
    [JsonPropertyName("organization")] public VIOrganizationDto Organization { get; init; } = new();
}

public record VIParticipantDto
{
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
}

#endregion viPlayer

#region viTeam
public record VITeamProductConfigDto
{
    [JsonPropertyName("team-registration")]
    public List<VITeamProductDto> TeamRegistration { get; init; } = new();
}

public record VITeamProductDto
{
    public VICustomerDto Customer { get; init; } = new();
    public VITeamMetadataDto Metadata { get; init; } = new();
    [JsonPropertyName("policy_attributes")] public VITeamPolicyAttributes PolicyAttributes { get; init; } = new();
}

public record VITeamObjectResponse
{
    [JsonPropertyName("client_id")] public string ClientId { get; init; } = string.Empty;
    public VIThemeDto Theme { get; init; } = new();
    [JsonPropertyName("product_config")] public VITeamProductConfigDto ProductConfig { get; init; } = new();
    public VIPaymentsDto Payments { get; init; } = new();
}

public record VITeamPolicyAttributes
{
    [JsonPropertyName("organization_name")] public string OrganizationName { get; init; } = string.Empty;
    [JsonPropertyName("organization_contact_name")] public string OrganizationContactName { get; init; } = string.Empty;
    [JsonPropertyName("organization_contact_email")] public string OrganizationContactEmail { get; init; } = string.Empty;
    [JsonPropertyName("teams")] public List<VITeamDto> Teams { get; init; } = new();
    [JsonPropertyName("event")] public VIEventDto JobEvent { get; init; } = new();
}

public record VITeamObjectRequest
{
    public string client_id { get; init; } = string.Empty;
    public VIThemeDto theme { get; init; } = new();
    public string unique_offer_id { get; init; } = string.Empty;
    public VIPaymentsDto payments { get; init; } = new();
    public VICustomerDto customer { get; init; } = new();
    public VITeamRegistrationDto product_config { get; init; } = new();
}

public record VIThemeDto
{
    public VIColorsDto? colors { get; init; }
    public string font_family { get; init; } = string.Empty;
    public VIComponentsDto components { get; init; } = new();
}

public record VIColorsDto
{
    public string background { get; init; } = string.Empty;
    public string primary { get; init; } = string.Empty;
    public string primary_contrast { get; init; } = string.Empty;
    public string secondary { get; init; } = string.Empty;
    public string secondary_contrast { get; init; } = string.Empty;
    public string neutral { get; init; } = string.Empty;
    public string neutral_contrast { get; init; } = string.Empty;
    public string error { get; init; } = string.Empty;
    public string error_contrast { get; init; } = string.Empty;
    public string success { get; init; } = string.Empty;
    public string success_contrast { get; init; } = string.Empty;
    public string border { get; init; } = string.Empty;
}

public record VIAddress
{
    public string street { get; init; } = string.Empty;
    public string city { get; init; } = string.Empty;
    public string state { get; init; } = string.Empty;
    public string zip { get; init; } = string.Empty;
}

public record VICustomerDto
{
    public string first_name { get; init; } = string.Empty;
    public string last_name { get; init; } = string.Empty;
    public string email_address { get; init; } = string.Empty;
    public string phone { get; init; } = string.Empty;
    public string street { get; init; } = string.Empty;
    public string city { get; init; } = string.Empty;
    public string state { get; init; } = string.Empty;
    public string postal_code { get; init; } = string.Empty;
}

public record VITeamDto
{
    public int insurable_amount { get; init; }
    public string team_name { get; init; } = string.Empty;
}

public record VIEventDto
{
    public string name { get; init; } = string.Empty;
    public string type { get; init; } = string.Empty;
    public string location { get; init; } = string.Empty;
    public VIAddress address { get; init; } = new();
    public string event_start_date { get; init; } = string.Empty;
    public string event_end_date { get; init; } = string.Empty;
}

public record VITeamRegistrationDto
{
    [JsonPropertyName("team-registration")]
    public List<VITeamProduct> team_registration { get; init; } = new();
}

public record VITeamMetadataDto
{
    public string tsic_secondchance { get; init; } = string.Empty;
    public string context_event { get; init; } = string.Empty;
    public string context_name { get; init; } = string.Empty;
    public string context_description { get; init; } = string.Empty;
    public Guid tsic_registrationid { get; init; }
    public Guid tsic_teamid { get; init; }
}

public record VITeamProduct
{
    public VICustomerDto customer { get; init; } = new();
    public VITeamMetadataDto metadata { get; init; } = new();

    public VITeamPolicyAttributes policy_attributes { get; init; } = new();
    public string offer_Id { get; init; } = string.Empty;
}

public record VIPaymentsDto
{
    public bool enabled { get; init; }
    public bool button { get; init; }
}

public record VIComponentsDto
{
    public string border_radius { get; init; } = string.Empty;
}

public record VIOrganizationDto
{
    public string org_name { get; init; } = string.Empty;
    public string org_contact_email { get; init; } = string.Empty;
    public string org_contact_first_name { get; init; } = string.Empty;
    public string org_contact_last_name { get; init; } = string.Empty;
    public string org_contact_phone { get; init; } = string.Empty;
    public string org_website { get; init; } = string.Empty;
    public string org_city { get; init; } = string.Empty;
    public string org_state { get; init; } = string.Empty;
    public string org_postal_code { get; init; } = string.Empty;
    public string org_country { get; init; } = string.Empty;
    public bool payment_plan { get; init; }
    public string registration_session_name { get; init; } = string.Empty;
}

public class VICreditCardDto
{
    public string name { get; init; } = string.Empty;
    public string number { get; init; } = string.Empty;
    public string verification { get; init; } = string.Empty;
    public string month { get; init; } = string.Empty;
    public string year { get; init; } = string.Empty;
    public string address_postal_code { get; init; } = string.Empty;
}

public class VIPaymentMethodDto
{
    public VICreditCardDto card { get; init; } = new();
    public bool save_for_future_use { get; init; }
}

public class VIMakePaymentDto
{
    public VICustomerDto customer { get; init; } = new();
    public string quote_id { get; init; } = string.Empty;
    public VIPaymentMethodDto payment_method { get; init; } = new();
}

public class VIMakePaymentPolicyHolderDto
{
    public string id { get; init; } = string.Empty;
    public string first_name { get; init; } = string.Empty;
    public string last_name { get; init; } = string.Empty;
    public string email_address { get; init; } = string.Empty;
    public string street { get; init; } = string.Empty;
    public string city { get; init; } = string.Empty;
    public string postal_code { get; init; } = string.Empty;
    public string country { get; init; } = string.Empty;
    public string state { get; init; } = string.Empty;
}

public class VIMakePaymentPartnerDto
{
    public string id { get; init; } = string.Empty;
    public string legal_business_name { get; init; } = string.Empty;
}

public class VIMakePaymentResponseDto
{
    public string id { get; init; } = string.Empty;
    public string master_policy_id { get; init; } = string.Empty;
    public string policy_number { get; init; } = string.Empty;
    public VIMakePaymentPolicyHolderDto policy_holder { get; init; } = new();
    public VIMakePaymentPartnerDto partner { get; init; } = new();
    public string policy_status { get; init; } = string.Empty;
    public string issued_date { get; init; } = string.Empty;
    public string expiration_date { get; init; } = string.Empty;
    public string effective_date { get; init; } = string.Empty;
    public string quote_date { get; init; } = string.Empty;
    public VITeamProduct product { get; init; } = new();
    public VITeamPolicyAttributes policy_attributes { get; init; } = new();
    public decimal premium_amount { get; init; }
    public string quote_id { get; init; } = string.Empty;
    public bool is_test { get; init; }
}

public class VIMakePlayerPaymentResponseDto
{
    public string id { get; init; } = string.Empty;
    public string policy_number { get; init; } = string.Empty;
    public VIMakePaymentPartnerDto partner { get; init; } = new();
    public string policy_status { get; init; } = string.Empty;
    public string issued_date { get; init; } = string.Empty;
    public string expiration_date { get; init; } = string.Empty;
    public string effective_date { get; init; } = string.Empty;
    public string quote_date { get; init; } = string.Empty;
    public VIPlayerProductDto product { get; init; } = new();
    public VIPlayerPolicyAttributes policy_attributes { get; init; } = new();
    public decimal premium_amount { get; init; }
    public string quote_id { get; init; } = string.Empty;
    public bool is_test { get; init; }
    public VIPlayerMetadataDto metadata { get; init; } = new();
    public VIMakePaymentPolicyHolderDto policy_holder { get; init; } = new();
}

public class VIMakeTeamPaymentResponseDto
{
    public string id { get; init; } = string.Empty;
    public string policy_number { get; init; } = string.Empty;
    public VIMakePaymentPartnerDto partner { get; init; } = new();
    public string policy_status { get; init; } = string.Empty;
    public string issued_date { get; init; } = string.Empty;
    public string expiration_date { get; init; } = string.Empty;
    public string effective_date { get; init; } = string.Empty;
    public string quote_date { get; init; } = string.Empty;
    public VIPlayerProductDto product { get; init; } = new();
    public VIPlayerPolicyAttributes policy_attributes { get; init; } = new();
    public decimal premium_amount { get; init; }
    public string quote_id { get; init; } = string.Empty;
    public bool is_test { get; init; }
    public VITeamMetadataDto metadata { get; init; } = new();
    public VIMakePaymentPolicyHolderDto policy_holder { get; init; } = new();
}

public class VIMakeTokenPaymentMethodDto
{
    public VICreditCardDto card { get; init; } = new();
    public string token { get; init; } = string.Empty;
}

public class VIMakeTokenCCPaymentDto
{
    public string quote_id { get; init; } = string.Empty;
    public VIMakeTokenPaymentMethodDto payment_method { get; init; } = new();
}

public class VIMakeTokenBatchCCPaymentDto
{
    public List<VIMakeTokenBatchQuotesDto> quotes { get; init; } = new();
    public VIMakeTokenBatchPaymentMethodDto payment_method { get; init; } = new();
}

public class VIMakeTokenBatchQuotesDto
{
    public string quote_id { get; init; } = string.Empty;
}
public class VIMakeTokenBatchPaymentMethodDto
{
    public VICreditCardDto card { get; init; } = new();
    public string token { get; init; } = string.Empty;
}
#endregion viTeam

