// Enriched DTO interfaces matching backend FamilyPlayersResponseDto

export interface RegistrationFinancials {
    feeBase: number;
    feeProcessing: number;
    feeDiscount: number;
    feeDonation: number;
    feeLateFee: number;
    feeTotal: number;
    owedTotal: number;
    paidTotal: number;
}

export interface FamilyPlayerRegistration {
    registrationId: string;
    active: boolean;
    financials: RegistrationFinancials;
    assignedTeamId?: string | null;
    assignedTeamName?: string | null;
    // Server now sends visible-only values as formFieldValues; keep formValues as client-normalized alias
    formValues: Record<string, any>;
    formFieldValues?: Record<string, any>;
    formFields?: RegistrationFormField[] | null;
}

export interface FamilyPlayer {
    playerId: string;
    firstName: string;
    lastName: string;
    gender: string;
    dob?: string;
    registered: boolean;
    selected: boolean;
    priorRegistrations: FamilyPlayerRegistration[];
}

export interface RegSaverDetails {
    policyNumber: string;
    policyCreateDate: string; // ISO date string
}

// Typed field metadata + current value (when provided)
export interface RegistrationFormField {
    name: string;
    dbColumn: string;
    displayName: string;
    inputType: string;
    dataSource?: string | null;
    options?: { value: string; label: string }[] | null;
    validation?: {
        required?: boolean;
        email?: boolean;
        requiredTrue?: boolean;
        minLength?: number;
        maxLength?: number;
        pattern?: string;
        min?: number;
        max?: number;
        compare?: string;
        remote?: string;
        message?: string;
    } | null;
    order: number;
    visibility: string;
    computed: boolean;
    conditionalOn?: { field: string; value: any; operator?: string } | null;
    value?: any; // seeded from latest registration snapshot for player-level fields
}

// Helper to normalize form values from API JsonElement payloads or plain objects
export function normalizeFormValues(raw: any): Record<string, any> {
    if (!raw || typeof raw !== 'object') return {};
    const result: Record<string, any> = {};
    for (const [k, v] of Object.entries(raw)) {
        result[k] = v as any;
    }
    return result;
}
