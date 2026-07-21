/**
 * Shared types for the v2 Player Registration Wizard.
 *
 * Types that originate in the backend (@core/api) are re-exported here
 * for convenience. Types local to the wizard (field schemas, form values,
 * payment options) are defined directly.
 */

import type { PaymentWaitlistedDto } from '@core/api';

// ── Re-exports from generated API models ──────────────────────────────
export type {
    AvailableTeamDto,
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
    PaymentWaitlistedDto,
} from '@core/api';

// ── Payment option (job-level) ────────────────────────────────────────
export type PaymentOption = 'PIF' | 'Deposit' | 'ARB';

// ── Form field value union (covers text, number, date, checkbox, select, multiselect) ──
export type PlayerFormFieldValue = string | number | boolean | null | string[];

// ── Profile field schema (parsed from job metadata JSON) ──────────────
export interface PlayerProfileFieldSchema {
    name: string;
    label: string;
    type: 'text' | 'number' | 'date' | 'select' | 'multiselect' | 'checkbox' | 'textarea' | 'upload';
    required: boolean;
    options: string[];
    placeholder: string | null;
    helpText: string | null;
    remoteUrl: string | null;
    errorMessage: string | null;
    // Numeric bounds migrated from legacy [Range] attributes (e.g. SAT 200–800, GPA 0–5).
    // Absent/null when the field has no range constraint.
    min?: number | null;
    max?: number | null;
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
    // Players who could not be seated on a full team and were placed on the
    // waitlist twin at payment time. Surfaced as a persistent notice on the
    // confirmation screen (replaces the transient post-Submit toast).
    waitlisted?: PaymentWaitlistedDto[];
}

// ── JSON helper type (avoids `any`/`unknown` in public fields) ────────
export type Json = string | number | boolean | null | Json[] | { [key: string]: Json };
