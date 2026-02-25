import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	RefereeSummaryDto,
	RefScheduleFilterOptionsDto,
	RefScheduleGameDto,
	RefScheduleSearchRequest,
	GameRefAssignmentDto,
	RefGameDetailsDto,
	RefereeCalendarEventDto,
	AssignRefsRequest,
	CopyGameRefsRequest,
	ImportRefereesResult,
	SeedTestRefsRequest,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class RefereeAssignmentService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/referee-assignment`;

	// ── Queries ──

	getReferees(): Observable<RefereeSummaryDto[]> {
		return this.http.get<RefereeSummaryDto[]>(`${this.base}/referees`);
	}

	getFilterOptions(): Observable<RefScheduleFilterOptionsDto> {
		return this.http.get<RefScheduleFilterOptionsDto>(`${this.base}/filter-options`);
	}

	searchSchedule(request: RefScheduleSearchRequest): Observable<RefScheduleGameDto[]> {
		return this.http.post<RefScheduleGameDto[]>(`${this.base}/search`, request);
	}

	getAllAssignments(): Observable<GameRefAssignmentDto[]> {
		return this.http.get<GameRefAssignmentDto[]>(`${this.base}/assignments`);
	}

	getGameDetails(gid: number): Observable<RefGameDetailsDto[]> {
		return this.http.get<RefGameDetailsDto[]>(`${this.base}/game-details/${gid}`);
	}

	getCalendarEvents(): Observable<RefereeCalendarEventDto[]> {
		return this.http.get<RefereeCalendarEventDto[]>(`${this.base}/calendar-events`);
	}

	// ── Commands ──

	assignRefs(request: AssignRefsRequest): Observable<void> {
		return this.http.post<void>(`${this.base}/assign`, request);
	}

	copyGameRefs(request: CopyGameRefsRequest): Observable<number[]> {
		return this.http.post<number[]>(`${this.base}/copy`, request);
	}

	importReferees(file: File): Observable<ImportRefereesResult> {
		const formData = new FormData();
		formData.append('file', file);
		return this.http.post<ImportRefereesResult>(`${this.base}/import`, formData);
	}

	seedTestReferees(count: number): Observable<RefereeSummaryDto[]> {
		return this.http.post<RefereeSummaryDto[]>(`${this.base}/seed-test`, { count } as SeedTestRefsRequest);
	}

	purgeAll(): Observable<void> {
		return this.http.delete<void>(`${this.base}/purge`);
	}
}
