import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@environments/environment';
import type { UsLaxValidationResultDto } from '@core/api';

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
	 * Ask the server whether this registrant may use this membership number on this job.
	 *
	 * The server decides — active status, Player involvement, expiry against the director's
	 * cutoff, and the lastname/DOB match — and returns only a verdict. The browser used to make
	 * that call itself off raw vendor JSON, which meant the checks it didn't implement simply
	 * never ran. Identity is part of the question, so lastName and dob are sent with it.
	 */
	checkEligibility(req: {
		membershipNumber: string;
		jobPath: string;
		lastName?: string | null;
		dob?: Date | null;
		teamId?: string | null;
	}): Observable<UsLaxValidationResultDto> {
		let params = new HttpParams()
			.set('number', req.membershipNumber)
			.set('jobPath', req.jobPath);
		if (req.lastName) params = params.set('lastName', req.lastName);
		// Date-only, local parts — toISOString() would shift the day backwards for anyone west of
		// UTC and turn every DOB match into a mismatch.
		if (req.dob) params = params.set('dob', toDateOnly(req.dob));
		if (req.teamId) params = params.set('teamId', req.teamId);

		return this.http.get<UsLaxValidationResultDto>(`${this.apiUrl}/validation/uslax`, { params });
	}

	/**
	 * Raw member record for the Tools → USLax Test panel. Admin-only endpoint: this returns the
	 * member's name, DOB and email, which is why it is not on the anonymous registration route.
	 */
	verify(membershipNumber: string): Observable<UsLaxMember | null> {
		return this.http
			.get<UsLaxApiResponse>(`${this.apiUrl}/uslax-membership/member/${encodeURIComponent(membershipNumber)}`)
			.pipe(map(res => res?.output ?? null));
	}
}

function toDateOnly(d: Date): string {
	const pad = (n: number) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
