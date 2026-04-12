import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { environment } from '@environments/environment';
import type { AdultRegistrationRequest, AdultRegistrationExistingRequest } from '@core/api';

// ── Response types (not auto-generated — controller returns IActionResult) ──

export interface AdultRegJobInfoResponse {
    jobId: string;
    jobName: string;
    availableRoles: AdultRoleOption[];
}

export interface AdultRoleOption {
    roleType: number;
    displayName: string;
    description: string;
}

export interface AdultRegFormResponse {
    roleType: number;
    fields: AdultRegField[];
    waivers: AdultWaiverDto[];
}

export interface AdultRegField {
    name: string;
    dbColumn: string;
    displayName: string;
    inputType: string;
    dataSource?: string | null;
    options?: Array<{ value: string; label: string }> | null;
    validation?: {
        required?: boolean;
        email?: boolean;
        requiredTrue?: boolean;
        minLength?: number | null;
        maxLength?: number | null;
        pattern?: string | null;
        min?: number | null;
        max?: number | null;
        message?: string | null;
    } | null;
    order: number;
    visibility: string;
    computed?: boolean;
    conditionalOn?: {
        field: string;
        operator: string;
        value: unknown;
    } | null;
}

export interface AdultWaiverDto {
    key: string;
    title: string;
    htmlContent: string;
}

export interface AdultRegistrationResponse {
    success: boolean;
    registrationId: string;
    message?: string | null;
}

export interface AdultConfirmationResponse {
    registrationId: string;
    confirmationHtml: string;
    roleDisplayName: string;
}

// ── PreSubmit types ──────────────────────────────────────────────

export interface PreSubmitAdultRequest {
    roleType: number;
    formValues: Record<string, unknown>;
    waiverAcceptance: Record<string, boolean>;
}

export interface PreSubmitAdultResponse {
    valid: boolean;
    validationErrors?: AdultValidationError[] | null;
    registrationId: string | null;
    fees: AdultFeeBreakdown;
}

export interface AdultFeeBreakdown {
    feeBase: number;
    feeProcessing: number;
    feeDiscount: number;
    feeLateFee: number;
    feeTotal: number;
    owedTotal: number;
}

export interface AdultValidationError {
    field: string;
    message: string;
}

// ── Payment types ────────────────────────────────────────────────

export interface AdultPaymentRequest {
    registrationId: string;
    creditCard?: CreditCardValues | null;
    paymentMethod: 'CC' | 'Check';
}

export interface CreditCardValues {
    number: string;
    expiry: string;
    code: string;
    firstName: string;
    lastName: string;
    address: string;
    zip: string;
    email: string;
    phone: string;
}

export interface AdultPaymentResponse {
    success: boolean;
    message?: string | null;
    transactionId?: string | null;
    errorCode?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdultRegistrationService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/adult-registration`;

    getJobInfo(jobPath: string) {
        return this.http.get<AdultRegJobInfoResponse>(`${this.apiUrl}/${jobPath}/job-info`);
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

    getConfirmation(registrationId: string, context?: HttpContext) {
        return this.http.get<AdultConfirmationResponse>(
            `${this.apiUrl}/confirmation/${registrationId}`,
            context ? { context } : undefined,
        );
    }

    resendConfirmationEmail(registrationId: string) {
        return this.http.post<{ message: string }>(`${this.apiUrl}/confirmation/${registrationId}/resend`, {});
    }

    preSubmit(jobPath: string, request: PreSubmitAdultRequest) {
        return this.http.post<PreSubmitAdultResponse>(`${this.apiUrl}/${jobPath}/pre-submit`, request);
    }

    submitPayment(request: AdultPaymentRequest) {
        return this.http.post<AdultPaymentResponse>(`${this.apiUrl}/submit-payment`, request);
    }
}
