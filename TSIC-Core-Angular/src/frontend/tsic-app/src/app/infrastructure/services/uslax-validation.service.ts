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
		/** Raw DOB string from the API. See toDateOnly — do NOT pass a Date built from a bare
		 *  ISO date, it has already lost a day to UTC parsing by the time it gets here. */
		dob?: string | Date | null;
		teamId?: string | null;
	}): Observable<UsLaxValidationResultDto> {
		let params = new HttpParams()
			.set('number', req.membershipNumber)
			.set('jobPath', req.jobPath);
		if (req.lastName) params = params.set('lastName', req.lastName);
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

/**
 * DOB as a bare YYYY-MM-DD, without letting a timezone touch it.
 *
 * A string that already starts with a date is passed through verbatim. Feeding it through
 * `new Date(...)` first would parse a bare ISO date as UTC midnight, and reading local parts
 * back returns the PREVIOUS day for any negative UTC offset — which turned every DOB into a
 * mismatch and rejected valid registrations.
 */
function toDateOnly(d: string | Date): string {
	if (typeof d === 'string') {
		const m = /^(\d{4}-\d{2}-\d{2})/.exec(d.trim());
		if (m) return m[1];
		d = new Date(d);
	}
	const pad = (n: number) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
