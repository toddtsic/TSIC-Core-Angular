import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@environments/environment';

// ── Shared USLax types (single source of truth) ─────────────────────

/** Raw member data returned by the USA Lacrosse MemberPing API. */
export interface UsLaxMember {
	membership_id: string;
	mem_status: string;
	exp_date: string;
	firstname: string;
	lastname: string;
	birthdate: string;
	gender: string;
	age_verified: string;
	email: string;
	postalcode: string;
	state: string;
	involvement: string[];
}

/** Envelope returned by the USALax API (proxied through our backend). */
export interface UsLaxApiResponse {
	status_code: number;
	output: UsLaxMember | null;
}

/** Per-player validation status tracked by the registration wizard. */
export interface UsLaxStatusEntry {
	value: string;
	status: 'idle' | 'validating' | 'valid' | 'invalid';
	message?: string;
	membership?: Record<string, unknown>;
}

// ── Service ─────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class UsLaxValidationService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = environment.apiUrl;

	/**
	 * Verify a membership number against the USALax API.
	 * Backend handles zero-padding and OAuth token management.
	 */
	verify(membershipNumber: string): Observable<UsLaxMember | null> {
		return this.http
			.get<UsLaxApiResponse>(`${this.apiUrl}/validation/uslax`, {
				params: { number: membershipNumber }
			})
			.pipe(map(res => res?.output ?? null));
	}
}
