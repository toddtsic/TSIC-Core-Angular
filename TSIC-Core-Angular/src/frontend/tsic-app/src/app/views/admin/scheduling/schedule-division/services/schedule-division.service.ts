import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AgegroupWithDivisionsDto,
    AutoScheduleResponse,
    DivisionPairingsResponse,
    DivisionTeamDto,
    WhoPlaysWhoResponse,
    ScheduleGridResponse,
    ScheduleGameDto,
    PlaceGameRequest,
    MoveGameRequest,
    DeleteDivGamesRequest,
    PairingDto
} from '@core/api';

// Re-export for consumers
export type {
    AgegroupWithDivisionsDto,
    AutoScheduleResponse,
    DivisionSummaryDto,
    DivisionPairingsResponse,
    DivisionTeamDto,
    WhoPlaysWhoResponse,
    PairingDto,
    ScheduleGridResponse,
    ScheduleGridRow,
    ScheduleFieldColumn,
    ScheduleGameDto,
    PlaceGameRequest,
    MoveGameRequest,
    DeleteDivGamesRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class ScheduleDivisionService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/schedule-division`;

    // ── Navigator ──

    getAgegroups(): Observable<AgegroupWithDivisionsDto[]> {
        return this.http.get<AgegroupWithDivisionsDto[]>(`${this.apiUrl}/agegroups`);
    }

    getDivisionPairings(divId: string): Observable<DivisionPairingsResponse> {
        return this.http.get<DivisionPairingsResponse>(`${this.apiUrl}/${divId}/pairings`);
    }

    getDivisionTeams(divId: string): Observable<DivisionTeamDto[]> {
        return this.http.get<DivisionTeamDto[]>(`${this.apiUrl}/${divId}/teams`);
    }

    getWhoPlaysWho(teamCount: number): Observable<WhoPlaysWhoResponse> {
        return this.http.get<WhoPlaysWhoResponse>(`${this.apiUrl}/who-plays-who`, {
            params: { teamCount: teamCount.toString() }
        });
    }

    // ── Schedule Grid ──

    getScheduleGrid(divId: string, agegroupId: string): Observable<ScheduleGridResponse> {
        return this.http.get<ScheduleGridResponse>(`${this.apiUrl}/${divId}/grid`, {
            params: { agegroupId }
        });
    }

    // ── Game Placement ──

    placeGame(request: PlaceGameRequest): Observable<ScheduleGameDto> {
        return this.http.post<ScheduleGameDto>(`${this.apiUrl}/place-game`, request);
    }

    moveGame(request: MoveGameRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/move-game`, request);
    }

    deleteGame(gid: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/game/${gid}`);
    }

    deleteDivisionGames(request: DeleteDivGamesRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/delete-div-games`, request);
    }

    // ── Auto-Schedule ──

    autoScheduleDiv(divId: string): Observable<AutoScheduleResponse> {
        return this.http.post<AutoScheduleResponse>(`${this.apiUrl}/auto-schedule/${divId}`, {});
    }
}
