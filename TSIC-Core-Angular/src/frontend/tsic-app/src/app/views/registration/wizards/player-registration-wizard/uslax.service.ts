import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '@environments/environment';

// Strict DTOs based on observed USLax response shape
export interface UsLaxMembershipDto {
    membership_id: string;
    mem_status: string;
    lastname: string;
    birthdate: string; // ISO date (yyyy-MM-dd)
    exp_date?: string; // ISO date (yyyy-MM-dd)
    age_verified?: string;
    firstname?: string;
    email?: string;
    gender?: string;
    involvement?: unknown;
    state?: string;
    postalcode?: string;
}

export interface UsLaxApiResponseDto {
    status_code: number;
    output: UsLaxMembershipDto;
}

@Injectable({ providedIn: 'root' })
export class UsLaxService {
    private readonly http = inject(HttpClient);

    // Resolve backend proxy endpoint (preferred) with optional override.
    // If environment.usLaxValidationUrl provided, use it; else build from environment.apiUrl.
    private getValidationUrl(): string {
        const env = environment as Record<string, unknown>;
        if (env['usLaxValidationUrl']) return String(env['usLaxValidationUrl']);
        const base: string = (env['apiUrl'] || '').toString();
        if (base) return `${base.replace(/\/$/, '')}/validation/uslax`;
        // Fallback to relative if apiUrl not configured (requires dev proxy to be set up)
        return '/api/validation/uslax';
    }

    // Left-pad the US Lax number to 12 digits (matches legacy server behavior)
    private padUsLaxNumber(no: string): string {
        // Use replaceAll to strip non-digits per lint preference
        const digits = String(no || '').replaceAll(/\D/g, '');
        return ("000000000000" + digits).slice(-12);
    }

    // Validate a US Lacrosse number via backend proxy. Optional context retained for future server use.
    validate(
        number: string,
        opts?: { lastName?: string; dob?: string | Date; validThrough?: string | Date }
    ) {
        const url = this.getValidationUrl();
        const padded = this.padUsLaxNumber(number);

        let params = new HttpParams().set('number', padded);
        if (opts?.lastName) params = params.set('lastName', String(opts.lastName).trim());
        if (opts?.dob) {
            const d = opts.dob instanceof Date ? opts.dob : new Date(opts.dob);
            if (!Number.isNaN(d.getTime())) params = params.set('dob', d.toISOString());
        }
        if (opts?.validThrough) {
            const vt = opts.validThrough instanceof Date ? opts.validThrough : new Date(opts.validThrough);
            if (!Number.isNaN(vt.getTime())) params = params.set('validThrough', vt.toISOString());
        }

        return this.http.get<UsLaxApiResponseDto>(url, { params });
    }
}
