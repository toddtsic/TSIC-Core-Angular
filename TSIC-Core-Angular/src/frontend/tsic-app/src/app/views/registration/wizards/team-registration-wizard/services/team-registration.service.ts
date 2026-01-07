import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import {
    TeamsMetadataResponse,
    RegisterTeamRequest,
    RegisterTeamResponse,
    AddClubTeamRequest,
    AddClubTeamResponse,
    ClubRepClubDto,
    AddClubToRepRequest,
    AddClubToRepResponse,
    ClubTeamManagementDto,
    UpdateClubTeamRequest,
    ClubTeamOperationResponse,
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
     * Add a new ClubTeam to the club
     * 
     * Creates a new ClubTeam record that will be available for all future events.
     * 
     * @param request - New team data (name, grade year, level of play)
     */
    addNewClubTeam(request: AddClubTeamRequest): Observable<AddClubTeamResponse> {
        return this.http.post<AddClubTeamResponse>(`${this.apiUrl}/add-club-team`, request);
    }

    /**
     * Add a club to the current user's rep account
     * 
     * @param clubName - Name of the club to add
     */
    addClubToRep(clubName: string): Observable<AddClubToRepResponse> {
        const request: AddClubToRepRequest = { clubName };
        return this.http.post<AddClubToRepResponse>(`${this.apiUrl}/add-club`, request);
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

    /**
     * Get all club teams for all clubs the user is a rep for
     * 
     * Returns teams with metadata including usage and registration status
     */
    getClubTeams(): Observable<ClubTeamManagementDto[]> {
        return this.http.get<ClubTeamManagementDto[]>(`${this.apiUrl}/club-library-teams`);
    }

    /**
     * Update a club team
     * 
     * Conditional logic:
     * - If team has never been registered: all fields can be updated
     * - If team has registration history: only level of play can be updated
     * 
     * @param request - Update request with clubTeamId and field values
     * @param onSuccess - Callback invoked on successful update with response
     * @param onError - Callback invoked on error with error message
     */
    updateClubTeam(
        request: UpdateClubTeamRequest,
        onSuccess: (response: ClubTeamOperationResponse) => void,
        onError: (error: string) => void
    ): void {
        this.http.put<ClubTeamOperationResponse>(`${this.apiUrl}/club-team/${request.clubTeamId}`, request)
            .subscribe({
                next: (response) => onSuccess(response),
                error: (err) => {
                    const message = err.error?.message || err.error?.Message || 'Failed to update team';
                    onError(message);
                }
            });
    }

    /**
     * Activate a club team (set Active = true)
     * 
     * @param clubTeamId - ID of the team to activate
     * @param onSuccess - Callback invoked on successful activation
     * @param onError - Callback invoked on error with error message
     */
    activateClubTeam(
        clubTeamId: number,
        onSuccess: (response: ClubTeamOperationResponse) => void,
        onError: (error: string) => void
    ): void {
        this.http.patch<ClubTeamOperationResponse>(`${this.apiUrl}/club-team/${clubTeamId}/activate`, {})
            .subscribe({
                next: (response) => onSuccess(response),
                error: (err) => {
                    const message = err.error?.message || err.error?.Message || 'Failed to activate team';
                    onError(message);
                }
            });
    }

    /**
     * Inactivate a club team (set Active = false)
     * 
     * Cannot inactivate if team is currently registered for any event
     * 
     * @param clubTeamId - ID of the team to inactivate
     * @param onSuccess - Callback invoked on successful inactivation
     * @param onError - Callback invoked on error with error message
     */
    inactivateClubTeam(
        clubTeamId: number,
        onSuccess: (response: ClubTeamOperationResponse) => void,
        onError: (error: string) => void
    ): void {
        this.http.patch<ClubTeamOperationResponse>(`${this.apiUrl}/club-team/${clubTeamId}/inactivate`, {})
            .subscribe({
                next: (response) => onSuccess(response),
                error: (err) => {
                    const message = err.error?.message || err.error?.Message || 'Failed to inactivate team';
                    onError(message);
                }
            });
    }

    /**
     * Delete a club team
     * 
     * Smart delete logic:
     * - If team has never been registered: hard delete (permanently removed)
     * - If team has registration history: soft delete (set Active = false)
     * Cannot delete if team is currently registered for any event
     * 
     * @param clubTeamId - ID of the team to delete
     * @param onSuccess - Callback invoked on successful deletion
     * @param onError - Callback invoked on error with error message
     */
    deleteClubTeam(
        clubTeamId: number,
        onSuccess: (response: ClubTeamOperationResponse) => void,
        onError: (error: string) => void
    ): void {
        this.http.delete<ClubTeamOperationResponse>(`${this.apiUrl}/club-team/${clubTeamId}`)
            .subscribe({
                next: (response) => onSuccess(response),
                error: (err) => {
                    const message = err.error?.message || err.error?.Message || 'Failed to delete team';
                    onError(message);
                }
            });
    }
}
