import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';

export interface ClubTeamManagementDto {
  clubTeamId: number;
  clubTeamName: string;
  clubTeamGradYear: string;
  clubTeamLevelOfPlay: string;
  isActive: boolean;
  hasBeenUsed: boolean;
}

export interface ClubTeamOperationResponse {
  success: boolean;
  clubTeamId: number;
  clubTeamName: string;
  message?: string;
}

@Injectable({
  providedIn: 'root'
})
export class ClubTeamManagementService {
  private readonly apiUrl = `${environment.apiUrl}/team-registration`;

  constructor(private readonly http: HttpClient) { }

  /**
   * Get all club teams (active + inactive) for management
   */
  getClubTeamsForManagement(clubName: string): Observable<ClubTeamManagementDto[]> {
    return this.http.get<ClubTeamManagementDto[]>(
      `${this.apiUrl}/clubs/${encodeURIComponent(clubName)}/management`
    );
  }

  /**
   * Inactivate a club team (soft delete)
   */
  inactivateClubTeam(clubTeamId: number): Observable<ClubTeamOperationResponse> {
    return this.http.patch<ClubTeamOperationResponse>(
      `${this.apiUrl}/teams/${clubTeamId}/inactivate`,
      {}
    );
  }

  /**
   * Activate a club team (restore from inactive)
   */
  activateClubTeam(clubTeamId: number): Observable<ClubTeamOperationResponse> {
    return this.http.patch<ClubTeamOperationResponse>(
      `${this.apiUrl}/teams/${clubTeamId}/activate`,
      {}
    );
  }

  /**
   * Delete a club team permanently (only if unused)
   */
  deleteClubTeam(clubTeamId: number): Observable<ClubTeamOperationResponse> {
    return this.http.delete<ClubTeamOperationResponse>(
      `${this.apiUrl}/teams/${clubTeamId}`
    );
  }
}
