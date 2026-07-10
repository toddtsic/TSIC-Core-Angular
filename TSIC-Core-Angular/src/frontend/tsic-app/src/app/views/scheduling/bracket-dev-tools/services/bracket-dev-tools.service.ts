import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AgegroupScopeRequest, BracketDevActionRequest, BracketDevActionResult } from '@core/api';

/**
 * Sandbox-only bracket exercise endpoints (backend gates on IsSandbox()).
 * Types come from @core/api (generated); only the HTTP wiring is hand-written,
 * per the codebase convention (no generated client services).
 */
@Injectable({ providedIn: 'root' })
export class BracketDevToolsService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/dev-scheduling/bracket`;

	clearScores(request: BracketDevActionRequest): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/clear-scores`, request);
	}

	autoScorePool(request: BracketDevActionRequest): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-pool`, request);
	}

	autoScoreRound(request: BracketDevActionRequest): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-round`, request);
	}

	// ── Job/event scope (View Schedule seed strip for reseeding tournaments) — pools live
	//    in their own agegroup and reseed the championship agegroups cross-agegroup, so the
	//    whole event is the unit of work. All three take no body (job from auth context).

	autoScorePoolJob(): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-pool-job`, {});
	}

	autoScoreRoundJob(): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-round-job`, {});
	}

	revertLeague(): Observable<BracketDevActionResult> {
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/revert-league`, {});
	}

	// ── Agegroup scope (View Schedule seed strip, scoped to a single selected age group) —
	//    each takes the agegroupId; job comes from auth context. Pool scoring still fires
	//    job-wide seed resolution, so a reseeding tournament's separate championship
	//    agegroups reseed automatically.

	autoScorePoolAgegroup(agegroupId: string): Observable<BracketDevActionResult> {
		const body: AgegroupScopeRequest = { agegroupId };
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-pool-agegroup`, body);
	}

	autoScoreRoundAgegroup(agegroupId: string): Observable<BracketDevActionResult> {
		const body: AgegroupScopeRequest = { agegroupId };
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/auto-score-round-agegroup`, body);
	}

	revertAgegroup(agegroupId: string): Observable<BracketDevActionResult> {
		const body: AgegroupScopeRequest = { agegroupId };
		return this.http.post<BracketDevActionResult>(`${this.apiUrl}/revert-agegroup`, body);
	}
}
