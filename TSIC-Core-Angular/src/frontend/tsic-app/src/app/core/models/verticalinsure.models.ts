// VerticalInsure minimal client-side models
// Intent: provide helpful typing for the frontend without fully duplicating backend DTOs.
// Fields are optional where reasonable to avoid tight coupling to backend changes.

export interface VIPlayerObjectResponse {
    client_id?: string;
    payments?: VIPayments;
    theme?: VITheme;
    product_config?: {
        registration_cancellation?: VIPlayerProduct[];
    };
}

export interface VIPayments {
    enabled?: boolean;
    button?: boolean;
}

export interface VITheme {
    colors?: Partial<VIColors> | null;
    font_family?: string;
    components?: Partial<VIComponents>;
}

export interface VIColors {
    background: string;
    primary: string;
    primary_contrast: string;
    secondary: string;
    secondary_contrast: string;
    neutral: string;
    neutral_contrast: string;
    error: string;
    error_contrast: string;
    success: string;
    success_contrast: string;
    border: string;
}

export interface VIComponents {
    border_radius: string;
}

export interface VIPlayerProduct {
    customer?: Partial<VICustomer>;
    metadata?: Partial<VIPlayerMetadata>;
    policy_attributes?: Partial<VIPlayerPolicyAttributes>;
    offer_Id?: string;
}

export interface VICustomer {
    first_name: string;
    last_name: string;
    email_address: string;
    phone: string;
    street: string;
    city: string;
    state: string;
    postal_code: string;
}

export interface VIPlayerMetadata {
    context_name: string;
    context_event: string;
    context_description: string;
    tsic_registrationid: string; // Guid (serialized)
    tsic_secondchance: string;
    tsic_customer: string;
}

export interface VIPlayerPolicyAttributes {
    event_start_date?: string | null; // Date-only string
    event_end_date?: string | null;   // Date-only string
    insurable_amount: number;
    participant?: VIParticipant;
    organization?: VIOrganization;
}

export interface VIParticipant {
    first_name: string;
    last_name: string;
}

export interface VIOrganization {
    org_name: string;
    org_contact_email: string;
    org_contact_first_name: string;
    org_contact_last_name: string;
    org_contact_phone: string;
    org_website: string;
    org_city: string;
    org_state: string;
    org_postal_code: string;
    org_country: string;
    payment_plan: boolean;
    registration_session_name: string;
}

export type VerticalInsureOfferState = {
    loading: boolean;
    data: VIPlayerObjectResponse | null;
    error: string | null;
};
