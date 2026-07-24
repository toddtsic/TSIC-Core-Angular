import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ClubRepRegistrationRequest, ClubRepRegistrationResponse, ClubSearchResult, AddClubRequest, AddClubResponse, ClubRepProfileDto, ClubRepProfileUpdateRequest, ClubRenameRequest, ClubRenameResponse, ClubAffectedJob, AdminClubRenameRequest, AdminClubRenameResponse } from '@core/api';

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
     * Add an additional club to an existing ClubRep user
     * Supports both creating new clubs and attaching to existing clubs
     */
    addClub(request: AddClubRequest): Observable<AddClubResponse> {
        return this.http.post<AddClubResponse>(`${this.clubRepsApiUrl}/add-club`, request);
    }

    /**
     * Search for clubs by name, state, or other criteria
     * Returns matching clubs with similarity scores
     */
    searchClubs(query?: string, state?: string): Observable<ClubSearchResult[]> {
        const params: Record<string, string> = {};
        if (query) params['q'] = query;
        if (state) params['state'] = state;

        return this.http.get<ClubSearchResult[]>(`${this.clubsApiUrl}/search`, { params });
    }

    /**
     * Read the authenticated user's profile fields (for the ClubRep profile edit page).
     */
    getSelfProfile(): Observable<ClubRepProfileDto> {
        return this.http.get<ClubRepProfileDto>(`${this.clubRepsApiUrl}/me`);
    }

    /**
     * Update the authenticated user's profile fields.
     */
    updateSelfProfile(request: ClubRepProfileUpdateRequest): Observable<void> {
        return this.http.put<void>(`${this.clubRepsApiUrl}/me`, request);
    }

    /**
     * Rename a club the authenticated user reps. Backend honors this only while the
     * club has no registered teams; otherwise responds with success=false + a message.
     */
    renameClub(request: ClubRenameRequest): Observable<ClubRenameResponse> {
        return this.http.put<ClubRenameResponse>(`${this.clubRepsApiUrl}/rename-club`, request);
    }

    /**
     * SuperUser: jobs holding teams for this club — the affected-jobs impact list for the
     * admin rename confirm modal.
     */
    getRenameImpact(clubId: number): Observable<ClubAffectedJob[]> {
        return this.http.get<ClubAffectedJob[]>(`${this.clubRepsApiUrl}/admin/rename-impact/${clubId}`);
    }

    /**
     * SuperUser: rename a club even once it has registered teams. Recomposes every affected
     * job's schedule. Backend responds success=false + a message on a guard failure (collision, etc.).
     */
    adminRenameClub(request: AdminClubRenameRequest): Observable<AdminClubRenameResponse> {
        return this.http.put<AdminClubRenameResponse>(`${this.clubRepsApiUrl}/admin/rename`, request);
    }
}
