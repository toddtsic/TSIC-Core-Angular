import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AgegroupWithDivisionsDto,
    AutoScheduleResponse,
    DeleteAgegroupGamesRequest,
    DivisionPairingsResponse,
    DivisionTeamDto,
    EditDivisionTeamRequest,
    ScheduleGridResponse,
    ScheduleGameDto,
    PlaceGameRequest,
    MoveGameRequest,
    DeleteDivGamesRequest,
    WhoPlaysWhoResponse
} from '@core/api';

// TODO: remove after API model regeneration — GameDateInfoDto will be auto-generated
export interface GameDateInfoDto {
    date: string;
    gameCount: number;
}

// Re-export for consumers
export type {
    AgegroupWithDivisionsDto,
    AutoScheduleResponse,
    DeleteAgegroupGamesRequest,
    DivisionSummaryDto,
    DivisionPairingsResponse,
    DivisionTeamDto,
    EditDivisionTeamRequest,
    PairingDto,
    ScheduleGridResponse,
    ScheduleGridRow,
    ScheduleFieldColumn,
    ScheduleGameDto,
    PlaceGameRequest,
    MoveGameRequest,
    DeleteDivGamesRequest,
    WhoPlaysWhoResponse
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

    // ── Schedule Grid ──

    getScheduleGrid(divId: string, agegroupId: string): Observable<ScheduleGridResponse> {
        return this.http.get<ScheduleGridResponse>(`${this.apiUrl}/${divId}/grid`, {
            params: { agegroupId }
        });
    }

    /** Full event grid — all games across all agegroups/divisions. Reuses rescheduler endpoint with empty filters. */
    getEventGrid(): Observable<ScheduleGridResponse> {
        return this.http.post<ScheduleGridResponse>(`${environment.apiUrl}/Rescheduler/grid`, {});
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

    deleteAgegroupGames(request: DeleteAgegroupGamesRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/delete-agegroup-games`, request);
    }

    // ── Team Editing (delegates to pairings endpoint — rank swap + schedule sync) ──

    editDivisionTeam(request: EditDivisionTeamRequest): Observable<DivisionTeamDto[]> {
        return this.http.put<DivisionTeamDto[]>(
            `${environment.apiUrl}/pairings/division-team`, request
        );
    }

    // ── Who Plays Who ──

    getWhoPlaysWho(teamCount: number): Observable<WhoPlaysWhoResponse> {
        return this.http.get<WhoPlaysWhoResponse>(`${this.apiUrl}/who-plays-who`, {
            params: { teamCount: teamCount.toString() }
        });
    }

    // ── Game Dates ──

    getGameDates(agegroupId?: string, divId?: string): Observable<GameDateInfoDto[]> {
        const params: Record<string, string> = {};
        if (agegroupId) params['agegroupId'] = agegroupId;
        if (divId) params['divId'] = divId;
        return this.http.get<GameDateInfoDto[]>(`${this.apiUrl}/game-dates`, { params });
    }

    // ── Auto-Schedule ──

    autoScheduleDiv(divId: string): Observable<AutoScheduleResponse> {
        return this.http.post<AutoScheduleResponse>(`${this.apiUrl}/auto-schedule/${divId}`, {});
    }
}
