import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    TeamRosterCountDto,
    CampPlayerDto,
    UpdateCampGroupsRequest,
    BulkUpdateCampGroupsRequest,
    CampGroupOptionsDto,
    BulkUpdateCampGroupsResponse
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class CampGroupsService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/camp-groups`;

    getTeams(): Observable<TeamRosterCountDto[]> {
        return this.http.get<TeamRosterCountDto[]>(`${this.apiUrl}/teams`);
    }

    getOptions(): Observable<CampGroupOptionsDto> {
        return this.http.get<CampGroupOptionsDto>(`${this.apiUrl}/options`);
    }

    getCampers(teamId: string): Observable<CampPlayerDto[]> {
        return this.http.get<CampPlayerDto[]>(`${this.apiUrl}/teams/${teamId}/campers`);
    }

    updateGroups(registrationId: string, request: UpdateCampGroupsRequest): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/registrations/${registrationId}`, request);
    }

    bulkUpdateGroups(request: BulkUpdateCampGroupsRequest): Observable<BulkUpdateCampGroupsResponse> {
        return this.http.patch<BulkUpdateCampGroupsResponse>(`${this.apiUrl}/registrations/bulk`, request);
    }
}
