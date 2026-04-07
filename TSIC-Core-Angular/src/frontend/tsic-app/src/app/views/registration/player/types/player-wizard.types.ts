/**
 * Shared types for the v2 Player Registration Wizard.
 *
 * Types that originate in the backend (@core/api) are re-exported here
 * for convenience. Types local to the wizard (field schemas, form values,
 * payment options) are defined directly.
 */

// ── Re-exports from generated API models ──────────────────────────────
export type {
    FamilyPlayerDto,
    FamilyPlayerRegistrationDto,
    FamilyPlayersResponseDto,
    FamilyUserSummaryDto,
    JobRegFormDto,
    CcInfoDto,
    RegSaverDetailsDto,
    PreSubmitPlayerRegistrationRequestDto,
    PreSubmitPlayerRegistrationResponseDto,
    PreSubmitTeamSelectionDto,
    PreSubmitInsuranceDto,
    PreSubmitValidationErrorDto,
    PlayerRegConfirmationDto,
    VIPlayerObjectResponse,
    AuthTokenResponse,
    JobMetadataResponse,
    RegistrationFinancialsDto,
} from '@core/api';

// ── Payment option (job-level) ────────────────────────────────────────
export type PaymentOption = 'PIF' | 'Deposit' | 'ARB';

// ── Form field value union (covers text, number, date, checkbox, select, multiselect) ──
export type PlayerFormFieldValue = string | number | boolean | null | string[];

// ── Profile field schema (parsed from job metadata JSON) ──────────────
export interface PlayerProfileFieldSchema {
    name: string;
    label: string;
    type: 'text' | 'number' | 'date' | 'select' | 'multiselect' | 'checkbox' | 'textarea';
    required: boolean;
    options: string[];
    placeholder: string | null;
    helpText: string | null;
    remoteUrl: string | null;
    errorMessage: string | null;
    visibility?: 'public' | 'adminOnly' | 'hidden';
    condition?: { field: string; value: unknown; operator?: string } | null;
}

// ── Waiver definition (parsed from job metadata) ──────────────────────
export interface WaiverDefinition {
    id: string;
    title: string;
    html: string;
    required: boolean;
    version: string;
}

// ── Family user (normalized from legacy API response) ─────────────────
export interface NormalizedFamilyUser {
    familyUserId: string;
    displayName: string;
    userName: string;
    firstName?: string;
    lastName?: string;
    address?: string;
    address1?: string;
    address2?: string;
    city?: string;
    state?: string;
    zipCode?: string;
    zip?: string;
    postalCode?: string;
    email?: string;
    phone?: string;
    ccInfo?: {
        firstName?: string;
        lastName?: string;
        streetAddress?: string;
        zip?: string;
        email?: string;
        phone?: string;
    };
}

// ── US Lacrosse validation status (single source: uslax-validation.service) ──
export type { UsLaxStatusEntry } from '@infrastructure/services/uslax-validation.service';

// ── Last payment summary (for confirmation step) ──────────────────────
export interface PaymentSummary {
    option: PaymentOption;
    amount: number;
    transactionId?: string;
    subscriptionId?: string;
    viPolicyNumber?: string | null;
    viPolicyCreateDate?: string | null;
    message?: string | null;
    paymentMethod?: 'CC' | 'Check';
}

// ── JSON helper type (avoids `any`/`unknown` in public fields) ────────
export type Json = string | number | boolean | null | Json[] | { [key: string]: Json };
