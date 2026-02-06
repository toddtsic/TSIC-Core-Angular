using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos.VerticalInsure;
#region viPlayer

public record RequestVIPlayerRequest
{
    public string JobName { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public string FamilyUserId { get; set; } = string.Empty;
}

public record RequestVITeamRequest
{
    public string JobName { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public Guid ClubRepRegId { get; set; }
}

public record VIPlayerObjectResponse
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;
    [JsonPropertyName("theme")]
    public VIThemeDto Theme { get; set; } = new();
    [JsonPropertyName("product_config")]
    public VIPlayerProductConfigDto ProductConfig { get; set; } = new();
    [JsonPropertyName("payments")]
    public VIPaymentsDto Payments { get; set; } = new();

}

public record VIPlayerProductConfigDto
{
    [JsonPropertyName("registration-cancellation")]
    public List<VIPlayerProductDto> RegistrationCancellation { get; set; } = new();
}

public record VIPlayerProductDto
{
    [JsonPropertyName("customer")]
    public VICustomerDto Customer { get; set; } = new();
    [JsonPropertyName("metadata")]
    public VIPlayerMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("policy_attributes")]
    public VIPlayerPolicyAttributes PolicyAttributes { get; set; } = new();
    [JsonPropertyName("offer_Id")]
    public string OfferId { get; set; } = string.Empty;

}

public record VIPlayerMetadataDto
{
    [JsonPropertyName("context_name")]
    public string ContextName { get; set; } = string.Empty;
    [JsonPropertyName("context_event")]
    public string ContextEvent { get; set; } = string.Empty;
    [JsonPropertyName("context_description")]
    public string ContextDescription { get; set; } = string.Empty;
    [JsonPropertyName("tsic_registrationid")]
    public Guid TsicRegistrationId { get; set; }
    [JsonPropertyName("tsic_secondchance")]
    public string TsicSecondChance { get; set; } = string.Empty;
    [JsonPropertyName("tsic_customer")]
    public string TsicCustomer { get; set; } = string.Empty;
}

public record VIPlayerPolicyAttributes
{
    [JsonPropertyName("event_start_date")]
    public DateOnly? EventStartDate { get; set; }
    [JsonPropertyName("event_end_date")]
    public DateOnly? EventEndDate { get; set; }
    [JsonPropertyName("insurable_amount")]
    public int InsurableAmount { get; set; }
    [JsonPropertyName("participant")]
    public VIParticipantDto Participant { get; set; } = new();
    [JsonPropertyName("organization")]
    public VIOrganizationDto Organization { get; set; } = new();
}

public record VIParticipantDto
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

#endregion viPlayer

#region viTeam
public record VITeamProductConfigDto
{
    [JsonPropertyName("team-registration")]
    public List<VITeamProductDto> team_registration { get; set; } = new();
}

public record VITeamProductDto
{
    public VICustomerDto customer { get; set; } = new();
    public VITeamMetadataDto metadata { get; set; } = new();

    public VITeamPolicyAttributes policy_attributes { get; set; } = new();
}

public record VITeamObjectResponse
{
    public string client_id { get; set; } = string.Empty;
    public VIThemeDto theme { get; set; } = new();
    public VITeamProductConfigDto product_config { get; set; } = new();
    public VIPaymentsDto payments { get; set; } = new();
}

public record VITeamPolicyAttributes
{
    public string organization_name { get; set; } = string.Empty;
    public string organization_contact_name { get; set; } = string.Empty;
    public string organization_contact_email { get; set; } = string.Empty;
    public List<VITeamDto> teams { get; set; } = new();
    [JsonPropertyName("event")]
    public VIEventDto job_event { get; set; } = new();
}

public record VITeamObjectRequest
{
    public string client_id { get; set; } = string.Empty;
    public VIThemeDto theme { get; set; } = new();
    public string unique_offer_id { get; set; } = string.Empty;
    public VIPaymentsDto payments { get; set; } = new();
    public VICustomerDto customer { get; set; } = new();
    public VITeamRegistrationDto product_config { get; set; } = new();
}

public record VIThemeDto
{
    public VIColorsDto? colors { get; set; }
    public string font_family { get; set; } = string.Empty;
    public VIComponentsDto components { get; set; } = new();
}

public record VIColorsDto
{
    public string background { get; set; } = string.Empty;
    public string primary { get; set; } = string.Empty;
    public string primary_contrast { get; set; } = string.Empty;
    public string secondary { get; set; } = string.Empty;
    public string secondary_contrast { get; set; } = string.Empty;
    public string neutral { get; set; } = string.Empty;
    public string neutral_contrast { get; set; } = string.Empty;
    public string error { get; set; } = string.Empty;
    public string error_contrast { get; set; } = string.Empty;
    public string success { get; set; } = string.Empty;
    public string success_contrast { get; set; } = string.Empty;
    public string border { get; set; } = string.Empty;
}

public record VIAddress
{
    public string street { get; set; } = string.Empty;
    public string city { get; set; } = string.Empty;
    public string state { get; set; } = string.Empty;
    public string zip { get; set; } = string.Empty;
}

public record VICustomerDto
{
    public string first_name { get; set; } = string.Empty;
    public string last_name { get; set; } = string.Empty;
    public string email_address { get; set; } = string.Empty;
    public string phone { get; set; } = string.Empty;
    public string street { get; set; } = string.Empty;
    public string city { get; set; } = string.Empty;
    public string state { get; set; } = string.Empty;
    public string postal_code { get; set; } = string.Empty;
}

public record VITeamDto
{
    public int insurable_amount { get; set; }
    public string team_name { get; set; } = string.Empty;
}

public record VIEventDto
{
    public string name { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
    public string location { get; set; } = string.Empty;
    public VIAddress address { get; set; } = new();
    public string event_start_date { get; set; } = string.Empty;
    public string event_end_date { get; set; } = string.Empty;
}

public record VITeamRegistrationDto
{
    [JsonPropertyName("team-registration")]
    public List<VITeamProduct> team_registration { get; set; } = new();
}

public record VITeamMetadataDto
{
    public string tsic_secondchance { get; set; } = string.Empty;
    public string context_event { get; set; } = string.Empty;
    public string context_name { get; set; } = string.Empty;
    public string context_description { get; set; } = string.Empty;
    public Guid tsic_registrationid { get; set; }
    public Guid tsic_teamid { get; set; }
}

public record VITeamProduct
{
    public VICustomerDto customer { get; set; } = new();
    public VITeamMetadataDto metadata { get; set; } = new();

    public VITeamPolicyAttributes policy_attributes { get; set; } = new();
    public string offer_Id { get; set; } = string.Empty;
}

public record VIPaymentsDto
{
    public bool enabled { get; set; }
    public bool button { get; set; }
}

public record VIComponentsDto
{
    public string border_radius { get; set; } = string.Empty;
}

public record VIOrganizationDto
{
    public string org_name { get; set; } = string.Empty;
    public string org_contact_email { get; set; } = string.Empty;
    public string org_contact_first_name { get; set; } = string.Empty;
    public string org_contact_last_name { get; set; } = string.Empty;
    public string org_contact_phone { get; set; } = string.Empty;
    public string org_website { get; set; } = string.Empty;
    public string org_city { get; set; } = string.Empty;
    public string org_state { get; set; } = string.Empty;
    public string org_postal_code { get; set; } = string.Empty;
    public string org_country { get; set; } = string.Empty;
    public bool payment_plan { get; set; }
    public string registration_session_name { get; set; } = string.Empty;
}

public record VICreditCardDto
{
    public required string name { get; init; } = string.Empty;
    public required string number { get; init; } = string.Empty;
    public required string verification { get; init; } = string.Empty;
    public required string month { get; init; } = string.Empty;
    public required string year { get; init; } = string.Empty;
    public required string address_postal_code { get; init; } = string.Empty;
}

public record VIPaymentMethodDto
{
    public required VICreditCardDto card { get; init; }
    public required bool save_for_future_use { get; init; }
}

public record VIMakePaymentDto
{
    public required VICustomerDto customer { get; init; } = new();
    public required string quote_id { get; init; } = string.Empty;
    public required VIPaymentMethodDto payment_method { get; init; }
}

public record VIMakePaymentPolicyHolderDto
{
    public required string id { get; init; } = string.Empty;
    public required string first_name { get; init; } = string.Empty;
    public required string last_name { get; init; } = string.Empty;
    public required string email_address { get; init; } = string.Empty;
    public required string street { get; init; } = string.Empty;
    public required string city { get; init; } = string.Empty;
    public required string postal_code { get; init; } = string.Empty;
    public required string country { get; init; } = string.Empty;
    public required string state { get; init; } = string.Empty;
}

public record VIMakePaymentPartnerDto
{
    public required string id { get; init; } = string.Empty;
    public required string legal_business_name { get; init; } = string.Empty;
}

public record VIMakePaymentResponseDto
{
    public required string id { get; init; } = string.Empty;
    public required string master_policy_id { get; init; } = string.Empty;
    public required string policy_number { get; init; } = string.Empty;
    public required VIMakePaymentPolicyHolderDto policy_holder { get; init; }
    public required VIMakePaymentPartnerDto partner { get; init; }
    public required string policy_status { get; init; } = string.Empty;
    public required string issued_date { get; init; } = string.Empty;
    public required string expiration_date { get; init; } = string.Empty;
    public required string effective_date { get; init; } = string.Empty;
    public required string quote_date { get; init; } = string.Empty;
    public required VITeamProduct product { get; init; } = new();
    public required VITeamPolicyAttributes policy_attributes { get; init; } = new();
    public required decimal premium_amount { get; init; }
    public required string quote_id { get; init; } = string.Empty;
    public required bool is_test { get; init; }
}

public record VIMakePlayerPaymentResponseDto
{
    public required string id { get; init; } = string.Empty;
    public required string policy_number { get; init; } = string.Empty;
    public required VIMakePaymentPartnerDto partner { get; init; }
    public required string policy_status { get; init; } = string.Empty;
    public required string issued_date { get; init; } = string.Empty;
    public required string expiration_date { get; init; } = string.Empty;
    public required string effective_date { get; init; } = string.Empty;
    public required string quote_date { get; init; } = string.Empty;
    public required VIPlayerProductDto product { get; init; } = new();
    public required VIPlayerPolicyAttributes policy_attributes { get; init; } = new();
    public required decimal premium_amount { get; init; }
    public required string quote_id { get; init; } = string.Empty;
    public required bool is_test { get; init; }
    public required VIPlayerMetadataDto metadata { get; init; } = new();
    public required VIMakePaymentPolicyHolderDto policy_holder { get; init; }
}

public record VIMakeTeamPaymentResponseDto
{
    public required string id { get; init; } = string.Empty;
    public required string policy_number { get; init; } = string.Empty;
    public required VIMakePaymentPartnerDto partner { get; init; }
    public required string policy_status { get; init; } = string.Empty;
    public required string issued_date { get; init; } = string.Empty;
    public required string expiration_date { get; init; } = string.Empty;
    public required string effective_date { get; init; } = string.Empty;
    public required string quote_date { get; init; } = string.Empty;
    public required VIPlayerProductDto product { get; init; } = new();
    public required VIPlayerPolicyAttributes policy_attributes { get; init; } = new();
    public required decimal premium_amount { get; init; }
    public required string quote_id { get; init; } = string.Empty;
    public required bool is_test { get; init; }
    public required VITeamMetadataDto metadata { get; init; } = new();
    public required VIMakePaymentPolicyHolderDto policy_holder { get; init; }
}

public record VIMakeTokenPaymentMethodDto
{
    public required VICreditCardDto card { get; init; }
    public required string token { get; init; } = string.Empty;
}

public record VIMakeTokenCCPaymentDto
{
    public required string quote_id { get; init; } = string.Empty;
    public required VIMakeTokenPaymentMethodDto payment_method { get; init; }
}

public record VIMakeTokenBatchCCPaymentDto
{
    public required List<VIMakeTokenBatchQuotesDto> quotes { get; init; } = new();
    public required VIMakeTokenBatchPaymentMethodDto payment_method { get; init; }
}

public record VIMakeTokenBatchQuotesDto
{
    public required string quote_id { get; init; } = string.Empty;
}
public record VIMakeTokenBatchPaymentMethodDto
{
    public required VICreditCardDto card { get; init; }
    public required string token { get; init; } = string.Empty;
}
#endregion viTeam
