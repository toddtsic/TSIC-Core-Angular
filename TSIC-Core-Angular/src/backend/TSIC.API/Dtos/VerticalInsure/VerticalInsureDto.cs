using System.Text.Json.Serialization;

namespace TSIC.API.Dtos.VerticalInsure;
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

public class VICreditCardDto
{
    public string name { get; set; } = string.Empty;
    public string number { get; set; } = string.Empty;
    public string verification { get; set; } = string.Empty;
    public string month { get; set; } = string.Empty;
    public string year { get; set; } = string.Empty;
    public string address_postal_code { get; set; } = string.Empty;
}

public class VIPaymentMethodDto
{
    public VICreditCardDto card { get; set; } = new();
    public bool save_for_future_use { get; set; }
}

public class VIMakePaymentDto
{
    public VICustomerDto customer { get; set; } = new();
    public string quote_id { get; set; } = string.Empty;
    public VIPaymentMethodDto payment_method { get; set; } = new();
}

public class VIMakePaymentPolicyHolderDto
{
    public string id { get; set; } = string.Empty;
    public string first_name { get; set; } = string.Empty;
    public string last_name { get; set; } = string.Empty;
    public string email_address { get; set; } = string.Empty;
    public string street { get; set; } = string.Empty;
    public string city { get; set; } = string.Empty;
    public string postal_code { get; set; } = string.Empty;
    public string country { get; set; } = string.Empty;
    public string state { get; set; } = string.Empty;
}

public class VIMakePaymentPartnerDto
{
    public string id { get; set; } = string.Empty;
    public string legal_business_name { get; set; } = string.Empty;
}

public class VIMakePaymentResponseDto
{
    public string id { get; set; } = string.Empty;
    public string master_policy_id { get; set; } = string.Empty;
    public string policy_number { get; set; } = string.Empty;
    public VIMakePaymentPolicyHolderDto policy_holder { get; set; } = new();
    public VIMakePaymentPartnerDto partner { get; set; } = new();
    public string policy_status { get; set; } = string.Empty;
    public string issued_date { get; set; } = string.Empty;
    public string expiration_date { get; set; } = string.Empty;
    public string effective_date { get; set; } = string.Empty;
    public string quote_date { get; set; } = string.Empty;
    public VITeamProduct product { get; set; } = new();
    public VITeamPolicyAttributes policy_attributes { get; set; } = new();
    public decimal premium_amount { get; set; }
    public string quote_id { get; set; } = string.Empty;
    public bool is_test { get; set; }
}

public class VIMakePlayerPaymentResponseDto
{
    public string id { get; set; } = string.Empty;
    public string policy_number { get; set; } = string.Empty;
    public VIMakePaymentPartnerDto partner { get; set; } = new();
    public string policy_status { get; set; } = string.Empty;
    public string issued_date { get; set; } = string.Empty;
    public string expiration_date { get; set; } = string.Empty;
    public string effective_date { get; set; } = string.Empty;
    public string quote_date { get; set; } = string.Empty;
    public VIPlayerProductDto product { get; set; } = new();
    public VIPlayerPolicyAttributes policy_attributes { get; set; } = new();
    public decimal premium_amount { get; set; }
    public string quote_id { get; set; } = string.Empty;
    public bool is_test { get; set; }
    public VIPlayerMetadataDto metadata { get; set; } = new();
    public VIMakePaymentPolicyHolderDto policy_holder { get; set; } = new();
}

public class VIMakeTeamPaymentResponseDto
{
    public string id { get; set; } = string.Empty;
    public string policy_number { get; set; } = string.Empty;
    public VIMakePaymentPartnerDto partner { get; set; } = new();
    public string policy_status { get; set; } = string.Empty;
    public string issued_date { get; set; } = string.Empty;
    public string expiration_date { get; set; } = string.Empty;
    public string effective_date { get; set; } = string.Empty;
    public string quote_date { get; set; } = string.Empty;
    public VIPlayerProductDto product { get; set; } = new();
    public VIPlayerPolicyAttributes policy_attributes { get; set; } = new();
    public decimal premium_amount { get; set; }
    public string quote_id { get; set; } = string.Empty;
    public bool is_test { get; set; }
    public VITeamMetadataDto metadata { get; set; } = new();
    public VIMakePaymentPolicyHolderDto policy_holder { get; set; } = new();
}

public class VIMakeTokenPaymentMethodDto
{
    public VICreditCardDto card { get; set; } = new();
    public string token { get; set; } = string.Empty;
}

public class VIMakeTokenCCPaymentDto
{
    public string quote_id { get; set; } = string.Empty;
    public VIMakeTokenPaymentMethodDto payment_method { get; set; } = new();
}

public class VIMakeTokenBatchCCPaymentDto
{
    public List<VIMakeTokenBatchQuotesDto> quotes { get; set; } = new();
    public VIMakeTokenBatchPaymentMethodDto payment_method { get; set; } = new();
}

public class VIMakeTokenBatchQuotesDto
{
    public string quote_id { get; set; } = string.Empty;
}
public class VIMakeTokenBatchPaymentMethodDto
{
    public VICreditCardDto card { get; set; } = new();
    public string token { get; set; } = string.Empty;
}
#endregion viTeam
