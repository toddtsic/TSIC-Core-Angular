import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';
import { ClubRosterPlayerDto } from '@core/api/models/ClubRosterPlayerDto';
import { ClubRosterMutationResultDto } from '@core/api/models/ClubRosterMutationResultDto';
import { MovePlayersRequest } from '@core/api/models/MovePlayersRequest';
import { DeletePlayersRequest } from '@core/api/models/DeletePlayersRequest';
import { UpdateUniformNumberRequest } from '@core/api/models/UpdateUniformNumberRequest';

@Injectable({ providedIn: 'root' })
export class ClubRosterService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/club-rosters`;

    getTeams(): Observable<ClubRosterTeamDto[]> {
        return this.http.get<ClubRosterTeamDto[]>(`${this.apiUrl}/teams`);
    }

    getRoster(teamId: string): Observable<ClubRosterPlayerDto[]> {
        return this.http.get<ClubRosterPlayerDto[]>(`${this.apiUrl}/teams/${teamId}/roster`);
    }

    movePlayers(request: MovePlayersRequest): Observable<ClubRosterMutationResultDto> {
        return this.http.put<ClubRosterMutationResultDto>(`${this.apiUrl}/move-players`, request);
    }

    deletePlayers(request: DeletePlayersRequest): Observable<ClubRosterMutationResultDto> {
        return this.http.post<ClubRosterMutationResultDto>(`${this.apiUrl}/delete-players`, request);
    }

    updateUniformNumber(request: UpdateUniformNumberRequest): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/uniform-number`, request);
    }
}
