import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import {
    TeamsMetadataResponse,
    RegisterTeamRequest,
    RegisterTeamResponse,

    ClubRepClubDto,
    AddClubToRepRequest,
    AddClubToRepResponse,

    CheckExistingRegistrationsResponse
} from '@core/api';

/**
 * Team Registration Service
 * 
 * Handles API communication for team registration workflow:
 * - Fetch teams metadata (club info, available ClubTeams, registered Teams, age groups)
 * - Register ClubTeam for event (creates Teams record)
 * - Unregister Team from event (deletes Teams record if unpaid)
 * - Add new ClubTeam to club
 * 
 * Backend endpoints (to be implemented):
 * - GET /api/team-registration/metadata?jobPath={jobPath}
 * - POST /api/team-registration/register-team
 * - DELETE /api/team-registration/unregister-team/{teamId}
 * - POST /api/team-registration/add-club-team
 */
@Injectable({
    providedIn: 'root'
})
export class TeamRegistrationService {
    private readonly apiUrl = `${environment.apiUrl}/team-registration`;
    private readonly http = inject(HttpClient);

    /**
     * Get list of clubs the current user is a rep for, with usage status
     */
    getMyClubs(): Observable<ClubRepClubDto[]> {
        return this.http.get<ClubRepClubDto[]>(`${this.apiUrl}/my-clubs`);
    }

    /**
     * Check if another club rep has already registered teams for this event+club.
     * Returns conflict info if another rep has teams registered.
     * 
     * @param jobPath - The event identifier (e.g., "summer-2025-soccer")
     * @param clubName - The club name to check
     */
    checkExistingRegistrations(jobPath: string, clubName: string): Observable<CheckExistingRegistrationsResponse> {
        const params = new HttpParams()
            .set('jobPath', jobPath)
            .set('clubName', clubName);
        return this.http.get<CheckExistingRegistrationsResponse>(`${this.apiUrl}/check-existing`, { params });
    }

    /**
     * Get teams metadata for the current club and event
     * 
     * Returns:
     * - Club info (clubId, clubName)
     * - Available ClubTeams (not yet registered for this event)
     * - Registered Teams (already registered for this event with financial details)
     * - Age groups (with availability, fees, slots)
     * 
     * @param jobPath - The event identifier (e.g., "summer-2025-soccer")
     * @param clubName - The club name to filter club rep association
     */
    getTeamsMetadata(jobPath: string, clubName: string): Observable<TeamsMetadataResponse> {
        const params = new HttpParams()
            .set('jobPath', jobPath)
            .set('clubName', clubName);
        return this.http.get<TeamsMetadataResponse>(`${this.apiUrl}/metadata`, { params });
    }

    /**
     * Register a ClubTeam for the current event
     * 
     * Creates a Teams record linking the ClubTeam to the current Job.
     * Validates age group availability before registration.
     * 
     * @param request - Registration request with clubTeamId and jobPath
     */
    registerTeamForEvent(request: RegisterTeamRequest): Observable<RegisterTeamResponse> {
        return this.http.post<RegisterTeamResponse>(`${this.apiUrl}/register-team`, request);
    }

    /**
     * Unregister a Team from the current event
     * 
     * Deletes the Teams record if it has no payments.
     * Returns error if team has payments (must contact support).
     * 
     * @param teamId - The Teams.TeamId to delete (Guid string)
     */
    unregisterTeamFromEvent(teamId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/unregister-team/${teamId}`);
    }

    /**
     * Remove a club from the current user's rep account
     * 
     * Only allowed if the club has no registered teams.
     * 
     * @param clubName - Name of the club to remove
     */
    removeClubFromRep(clubName: string): Observable<void> {
        const params = new HttpParams().set('clubName', clubName);
        return this.http.delete<void>(`${this.apiUrl}/remove-club`, { params });
    }

    /**
     * Update/rename a club name
     * 
     * Only allowed if the club has no registered teams.
     * 
     * @param oldClubName - Current name of the club
     * @param newClubName - New name for the club
     */
    updateClubName(oldClubName: string, newClubName: string): Observable<void> {
        const request = { oldClubName, newClubName };
        return this.http.patch<void>(`${this.apiUrl}/update-club-name`, request);
    }
}


