import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
    TeamsMetadataResponse,
    RegisterTeamRequest,
    RegisterTeamResponse,
    AddClubTeamRequest,
    AddClubTeamResponse
} from '../../../core/api/models';

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

    constructor(private readonly http: HttpClient) { }

    /**
     * Get list of clubs the current user is a rep for
     */
    getMyClubs(): Observable<string[]> {
        return this.http.get<string[]>(`${this.apiUrl}/my-clubs`);
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
     * Add a new ClubTeam to the club
     * 
     * Creates a new ClubTeam record that will be available for all future events.
     * 
     * @param request - New team data (name, grade year, level of play)
     */
    addNewClubTeam(request: AddClubTeamRequest): Observable<AddClubTeamResponse> {
        return this.http.post<AddClubTeamResponse>(`${this.apiUrl}/add-club-team`, request);
    }
}
