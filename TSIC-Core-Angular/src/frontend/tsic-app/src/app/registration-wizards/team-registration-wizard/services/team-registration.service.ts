import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

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
     * Get teams metadata for the current club and event
     * 
     * Returns:
     * - Club info (clubId, clubName)
     * - Available ClubTeams (not yet registered for this event)
     * - Registered Teams (already registered for this event with financial details)
     * - Age groups (with availability, fees, slots)
     * 
     * @param jobPath - The event identifier (e.g., "summer-2025-soccer")
     */
    getTeamsMetadata(jobPath: string): Observable<TeamsMetadataResponse> {
        const params = new HttpParams().set('jobPath', jobPath);
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

// ========================================
// Request/Response Types
// ========================================
// These will eventually be moved to core/api/models once backend endpoints are implemented

export interface TeamsMetadataResponse {
    clubId: number;
    clubName: string;
    availableClubTeams: ClubTeamDto[];
    registeredTeams: RegisteredTeamDto[];
    ageGroups: AgeGroupDto[];
}

export interface ClubTeamDto {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
}

export interface RegisteredTeamDto {
    teamId: string;          // Guid
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
    ageGroupId: string;      // Guid
    ageGroupName: string;

    // Financial details
    feeBase: number;          // RosterFee or (RosterFee + TeamFee) depending on bTeamsFullPaymentRequired
    feeProcessing: number;    // Credit card processing fee
    feeTotal: number;         // feeBase + feeProcessing
    paidTotal: number;        // Total amount paid so far
    owedTotal: number;        // feeTotal - paidTotal
}

export interface AgeGroupDto {
    ageGroupId: string;      // Guid
    ageGroupName: string;
    maxTeams: number;         // Maximum teams allowed
    registeredCount: number;  // Current number of registered teams
    rosterFee: number;        // Deposit amount
    teamFee: number;          // Final balance amount
}

export interface RegisterTeamRequest {
    clubTeamId: number;
    jobPath: string;
    ageGroupId?: string;     // Optional Guid: auto-determine from team's grade year if not provided
}

export interface RegisterTeamResponse {
    teamId: string;          // Guid
    success: boolean;
    message?: string;
}

export interface AddClubTeamRequest {
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
}

export interface AddClubTeamResponse {
    clubTeamId: number;
    success: boolean;
    message?: string;
}
