import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { BracketDevActionRequest, BracketDevActionResult } from '@core/api';

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
}
