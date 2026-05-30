import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    TeamRosterCountDto,
    TeamCheckinRowDto,
    PlayerCheckinRowDto,
    CheckinStateDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class CheckinService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/checkin`;

    /** Active teams in the job with player counts — team picker for player-mode. */
    getTeams(): Observable<TeamRosterCountDto[]> {
        return this.http.get<TeamRosterCountDto[]>(`${this.apiUrl}/teams`);
    }

    /** Team check-in roster (Tournament / League): teams with clubrep balance + status. */
    getTeamRoster(): Observable<TeamCheckinRowDto[]> {
        return this.http.get<TeamCheckinRowDto[]>(`${this.apiUrl}/team-roster`);
    }

    /** Player check-in roster (Camp / Tryouts): players on a team with balance, docs, status. */
    getPlayers(teamId: string): Observable<PlayerCheckinRowDto[]> {
        return this.http.get<PlayerCheckinRowDto[]>(`${this.apiUrl}/teams/${teamId}/players`);
    }

    checkInPlayer(registrationId: string): Observable<CheckinStateDto> {
        return this.http.post<CheckinStateDto>(`${this.apiUrl}/players/${registrationId}`, {});
    }

    undoPlayer(registrationId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/players/${registrationId}`);
    }

    checkInTeam(teamId: string): Observable<CheckinStateDto> {
        return this.http.post<CheckinStateDto>(`${this.apiUrl}/teams/${teamId}`, {});
    }

    undoTeam(teamId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/teams/${teamId}`);
    }

    /** Stream a player's med-form PDF as a blob (endpoint is auth-gated). */
    viewMedForm(playerUserId: string): Observable<Blob> {
        return this.http.get(`${environment.apiUrl}/files/medform/${playerUserId}`, {
            responseType: 'blob',
        });
    }
}
