import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ClubRepRegistrationRequest, ClubRepRegistrationResponse, ClubSearchResult } from '@core/api';

@Injectable({
    providedIn: 'root'
})
export class ClubService {
    private readonly http = inject(HttpClient);
    private readonly clubRepsApiUrl = `${environment.apiUrl}/club-reps`;
    private readonly clubsApiUrl = `${environment.apiUrl}/clubs`;

    /**
     * Register a new club and club rep account
     * Returns success status, clubId, userId, and any similar clubs found
     */
    registerClub(request: ClubRepRegistrationRequest): Observable<ClubRepRegistrationResponse> {
        return this.http.post<ClubRepRegistrationResponse>(`${this.clubRepsApiUrl}/register`, request);
    }

    /**
     * Search for clubs by name, state, or other criteria
     * Returns matching clubs with similarity scores
     */
    searchClubs(clubName?: string, state?: string): Observable<ClubSearchResult[]> {
        const params: any = {};
        if (clubName) params.clubName = clubName;
        if (state) params.state = state;

        return this.http.get<ClubSearchResult[]>(`${this.clubsApiUrl}/search`, { params });
    }
}
