import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    ScheduleFilterOptionsDto,
    ScheduleFilterRequest,
    ScheduleCapabilitiesDto,
    ViewGameDto,
    StandingsByDivisionResponse,
    TeamResultDto,
    DivisionBracketResponse,
    ContactDto,
    FieldDisplayDto,
    EditScoreRequest,
    EditGameRequest
} from '@core/api';

// Re-export for consumers
export type {
    ScheduleFilterOptionsDto,
    ScheduleFilterRequest,
    ScheduleCapabilitiesDto,
    CadtClubNode,
    CadtAgegroupNode,
    CadtDivisionNode,
    CadtTeamNode,
    FieldSummaryDto,
    ViewGameDto,
    StandingsByDivisionResponse,
    StandingsDto,
    DivisionStandingsDto,
    TeamResultDto,
    DivisionBracketResponse,
    BracketMatchDto,
    ContactDto,
    FieldDisplayDto,
    EditScoreRequest,
    EditGameRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class ViewScheduleService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/view-schedule`;

    // ── Filter Options & Capabilities ──

    getFilterOptions(jobPath?: string): Observable<ScheduleFilterOptionsDto> {
        if (jobPath) {
            return this.http.get<ScheduleFilterOptionsDto>(`${this.apiUrl}/filter-options`, { params: { jobPath } });
        }
        return this.http.get<ScheduleFilterOptionsDto>(`${this.apiUrl}/filter-options`);
    }

    getCapabilities(jobPath?: string): Observable<ScheduleCapabilitiesDto> {
        if (jobPath) {
            return this.http.get<ScheduleCapabilitiesDto>(`${this.apiUrl}/capabilities`, { params: { jobPath } });
        }
        return this.http.get<ScheduleCapabilitiesDto>(`${this.apiUrl}/capabilities`);
    }

    // ── Tab Data (POST with filter body) ──

    getGames(request: ScheduleFilterRequest, jobPath?: string): Observable<ViewGameDto[]> {
        if (jobPath) {
            return this.http.post<ViewGameDto[]>(`${this.apiUrl}/games`, request, { params: { jobPath } });
        }
        return this.http.post<ViewGameDto[]>(`${this.apiUrl}/games`, request);
    }

    getStandings(request: ScheduleFilterRequest, jobPath?: string): Observable<StandingsByDivisionResponse> {
        if (jobPath) {
            return this.http.post<StandingsByDivisionResponse>(`${this.apiUrl}/standings`, request, { params: { jobPath } });
        }
        return this.http.post<StandingsByDivisionResponse>(`${this.apiUrl}/standings`, request);
    }

    getTeamRecords(request: ScheduleFilterRequest, jobPath?: string): Observable<StandingsByDivisionResponse> {
        if (jobPath) {
            return this.http.post<StandingsByDivisionResponse>(`${this.apiUrl}/team-records`, request, { params: { jobPath } });
        }
        return this.http.post<StandingsByDivisionResponse>(`${this.apiUrl}/team-records`, request);
    }

    getBrackets(request: ScheduleFilterRequest, jobPath?: string): Observable<DivisionBracketResponse[]> {
        if (jobPath) {
            return this.http.post<DivisionBracketResponse[]>(`${this.apiUrl}/brackets`, request, { params: { jobPath } });
        }
        return this.http.post<DivisionBracketResponse[]>(`${this.apiUrl}/brackets`, request);
    }

    getContacts(request: ScheduleFilterRequest): Observable<ContactDto[]> {
        return this.http.post<ContactDto[]>(`${this.apiUrl}/contacts`, request);
    }

    // ── Drill-down & Details ──

    getTeamResults(teamId: string, jobPath?: string): Observable<TeamResultDto[]> {
        if (jobPath) {
            return this.http.get<TeamResultDto[]>(`${this.apiUrl}/team-results/${teamId}`, { params: { jobPath } });
        }
        return this.http.get<TeamResultDto[]>(`${this.apiUrl}/team-results/${teamId}`);
    }

    getFieldInfo(fieldId: string): Observable<FieldDisplayDto> {
        return this.http.get<FieldDisplayDto>(`${this.apiUrl}/field-info/${fieldId}`);
    }

    // ── Score Editing ──

    quickEditScore(request: EditScoreRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/quick-score`, request);
    }

    editGame(request: EditGameRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/edit-game`, request);
    }
}
