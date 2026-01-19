import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import {
    TeamsMetadataResponse,
    RegisterTeamRequest,
    RegisterTeamResponse,
    InitializeRegistrationRequest,
    AuthTokenResponse,
    ClubRepClubDto,
    CheckExistingRegistrationsResponse
} from '@core/api';

/**
 * Team Registration Service
 * 
 * Handles API communication for team registration workflow:
 * - Initialize registration (creates Registration record, returns Phase 2 token)
 * - Fetch teams metadata (club info, available ClubTeams, registered Teams, age groups)
 * - Register ClubTeam for event (creates Teams record)
 * - Unregister Team from event (deletes Teams record if unpaid)
 * 
 * Backend endpoints:
 * - POST /api/team-registration/initialize-registration (returns Phase 2 token with regId)
 * - GET /api/team-registration/metadata (uses regId from token)
 * - POST /api/team-registration/register-team (uses regId from token)
 * - DELETE /api/team-registration/unregister-team/{teamId}
 */
@Injectable({
    providedIn: 'root'
})
export class TeamRegistrationService {
    private readonly apiUrl = `${environment.apiUrl}/team-registration`;
    private readonly http = inject(HttpClient);
    private readonly authService = inject(AuthService);

    /**
     * Get list of clubs the current user is a rep for, with usage status
     */
    getMyClubs(): Observable<ClubRepClubDto[]> {
        return this.http.get<ClubRepClubDto[]>(`${this.apiUrl}/my-clubs`);
    }

    /**
     * Initialize registration after club selection.
     * Creates or finds Registration record and returns Phase 2 token with regId.
     * Automatically updates auth token and current user.
     * 
     * @param clubName - The club name to initialize registration for
     * @param jobPath - The event identifier (e.g., "aim-cac-2026")
     * @returns Phase 2 token response with accessToken containing regId claim
     */
    initializeRegistration(clubName: string, jobPath: string): Observable<AuthTokenResponse> {
        const request: InitializeRegistrationRequest = { clubName, jobPath };
        return this.http.post<AuthTokenResponse>(`${this.apiUrl}/initialize-registration`, request)
            .pipe(
                tap(response => {
                    // Update token using private methods (same pattern as selectRegistration)
                    if (response.accessToken) {
                        this.authService['setToken'](response.accessToken);
                        this.authService['initializeFromToken']();
                    }
                })
            );
    }

    /**
     * Get teams metadata for the current club and event.
     * Context (clubName, jobId) derived from regId token claim on backend.
     * 
     * Returns:
     * - Club info (clubId, clubName)
     * - Available ClubTeams (not yet registered for this event)
     * - Registered Teams (already registered for this event with financial details)
     * - Age groups (with availability, fees, slots)
     * 
     * @param bPayBalanceDue - Optional flag for payment balance calculation
     */
    getTeamsMetadata(bPayBalanceDue: boolean = false): Observable<TeamsMetadataResponse> {
        const params = new HttpParams().set('bPayBalanceDue', bPayBalanceDue.toString());
        return this.http.get<TeamsMetadataResponse>(`${this.apiUrl}/metadata`, { params });
    }

    /**
     * Register a ClubTeam for the current event.
     * Context (clubName, jobId) derived from regId token claim on backend.
     * 
     * Creates a Teams record linking the ClubTeam to the current Job.
     * Validates age group availability before registration.
     * 
     * @param request - Registration request with teamName, ageGroupId, levelOfPlay
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

    /**
     * Check for existing registrations that may conflict with a new registration.
     * 
     * Calls GET /api/team-registration/check-existing with jobPath and clubName.
     * Returns summary indicating whether conflicts exist and details if present.
     * Backend validates jobPath is non-empty and matches registration context.
     * 
     * @param jobPath - The event identifier (e.g., "aim-cac-2026")
     * @param clubName - The club name to check
     */
    checkExistingRegistrations(jobPath: string, clubName: string): Observable<CheckExistingRegistrationsResponse> {
        const params = new HttpParams()
            .set('jobPath', jobPath)
            .set('clubName', clubName);
        return this.http.get<CheckExistingRegistrationsResponse>(`${this.apiUrl}/check-existing`, { params });
    }

    /**
     * Accept the refund policy for the club rep's registration
     * 
     * Records BWaiverSigned3 = true on the Registration record.
     * Returns success message when recorded.
     */
    acceptRefundPolicy(): Observable<{ message: string }> {
        return this.http.post<{ message: string }>(`${this.apiUrl}/accept-refund-policy`, {});
    }
}
