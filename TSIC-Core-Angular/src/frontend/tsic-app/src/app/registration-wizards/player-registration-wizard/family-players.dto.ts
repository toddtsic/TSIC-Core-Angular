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
    formValues: Record<string, any>;
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

// Helper to normalize form values from API JsonElement payloads or plain objects
export function normalizeFormValues(raw: any): Record<string, any> {
    if (!raw || typeof raw !== 'object') return {};
    const result: Record<string, any> = {};
    for (const [k, v] of Object.entries(raw)) {
        result[k] = v as any;
    }
    return result;
}
