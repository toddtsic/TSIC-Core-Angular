import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { environment } from '@environments/environment';
import type {
    AdultRegJobInfoResponse,
    AdultRegFormResponse,
    AdultRegistrationRequest,
    AdultRegistrationExistingRequest,
    AdultRegistrationResponse,
    AdultConfirmationResponse,
    AdultTeamOptionDto,
    AdultRoleConfigDto,
    AdultExistingRegistrationDto,
    PreSubmitAdultRegRequestDto,
    PreSubmitAdultRegResponseDto,
    AdultPaymentRequestDto,
    AdultPaymentResponseDto,
    UsLaxVerifyBeginResponseDto,
    UsLaxVerifyConfirmResponseDto,
} from '@core/api';

// ── Role keys ────────────────────────────────────────────────────
// NOT a backend DTO — a frontend-only URL-key allowlist + guard. The backend
// resolves the key (+ JobTypeId) to an actual RoleId server-side.

/** Mirrors AdultRegRoleKeys (backend). Allowed URL role keys. */
export const ADULT_REG_ROLE_KEYS = ['coach', 'referee', 'recruiter', 'unassigned'] as const;
export type AdultRegRoleKey = typeof ADULT_REG_ROLE_KEYS[number];
export function isValidAdultRegRoleKey(v: string | null | undefined): v is AdultRegRoleKey {
    return v != null && (ADULT_REG_ROLE_KEYS as readonly string[]).includes(v.trim().toLowerCase());
}

@Injectable({ providedIn: 'root' })
export class AdultRegistrationService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/adult-registration`;

    getJobInfo(jobPath: string) {
        return this.http.get<AdultRegJobInfoResponse>(`${this.apiUrl}/${jobPath}/job-info`);
    }

    getAvailableTeams(jobPath: string) {
        return this.http.get<AdultTeamOptionDto[]>(`${this.apiUrl}/${jobPath}/available-teams`);
    }

    getRoleConfig(jobPath: string, roleKey: string) {
        return this.http.get<AdultRoleConfigDto>(`${this.apiUrl}/${jobPath}/role-config/${roleKey}`);
    }

    getMyExistingRegistration(jobPath: string, roleKey: string) {
        return this.http.get<AdultExistingRegistrationDto>(`${this.apiUrl}/${jobPath}/my-registration/${roleKey}`);
    }

    getFormSchema(jobPath: string, roleType: number) {
        return this.http.get<AdultRegFormResponse>(`${this.apiUrl}/${jobPath}/form-schema/${roleType}`);
    }

    registerNewUser(jobPath: string, request: AdultRegistrationRequest) {
        return this.http.post<AdultRegistrationResponse>(`${this.apiUrl}/${jobPath}/register`, request);
    }

    registerExistingUser(request: AdultRegistrationExistingRequest) {
        return this.http.post<AdultRegistrationResponse>(`${this.apiUrl}/register-existing`, request);
    }

    /**
     * Completes the registration: fetches the confirmation content AND triggers the registrant's
     * confirmation email server-side. POST because it is the wizard's terminal action with a side
     * effect, not a cacheable read. Idempotent — the server guards on BConfirmationSent.
     */
    getConfirmation(registrationId: string, context?: HttpContext) {
        return this.http.post<AdultConfirmationResponse>(
            `${this.apiUrl}/confirmation/${registrationId}`,
            {},
            context ? { context } : undefined,
        );
    }

    resendConfirmationEmail(registrationId: string) {
        return this.http.post<{ sent: boolean; message: string }>(
            `${this.apiUrl}/confirmation/${registrationId}/resend`, {});
    }

    preSubmit(jobPath: string, request: PreSubmitAdultRegRequestDto) {
        return this.http.post<PreSubmitAdultRegResponseDto>(`${this.apiUrl}/${jobPath}/pre-submit`, request);
    }

    submitPayment(request: AdultPaymentRequestDto) {
        return this.http.post<AdultPaymentResponseDto>(`${this.apiUrl}/submit-payment`, request);
    }

    /** Begin coach USLax identity verification — validates the number and emails a code
     *  to the address USA Lacrosse has on file. */
    beginUsLaxVerify(jobPath: string, sportAssnId: string) {
        return this.http.post<UsLaxVerifyBeginResponseDto>(
            `${this.apiUrl}/${jobPath}/uslax-verify/begin`, { sportAssnId });
    }

    /** Confirm the code the registrant entered against an outstanding verification. */
    confirmUsLaxVerify(jobPath: string, verificationId: string, code: string) {
        return this.http.post<UsLaxVerifyConfirmResponseDto>(
            `${this.apiUrl}/${jobPath}/uslax-verify/confirm`, { verificationId, code });
    }
}
