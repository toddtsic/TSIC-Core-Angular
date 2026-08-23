import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  PushNotificationDeviceCountDto,
  PushNotificationReadinessDto,
  PushNotificationHistoryDto,
  SendPushNotificationResponse,
  PushTeamOptionDto,
  TeamPushDto
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/push-notifications`;

  getReadiness(): Observable<PushNotificationReadinessDto> {
    return this.http.get<PushNotificationReadinessDto>(`${this.apiUrl}/readiness`);
  }

  getDeviceCount(): Observable<PushNotificationDeviceCountDto> {
    return this.http.get<PushNotificationDeviceCountDto>(`${this.apiUrl}/device-count`);
  }

  /** Teams in this job, for the audience selector. */
  availableTeams(): Observable<PushTeamOptionDto[]> {
    return this.http.get<PushTeamOptionDto[]>(`${this.apiUrl}/available-teams`);
  }

  /** Everyone this job's mobile app reaches. */
  sendPush(pushText: string): Observable<SendPushNotificationResponse> {
    return this.http.post<SendPushNotificationResponse>(`${this.apiUrl}/send`, { pushText });
  }

  /**
   * Just this team's subscribers. Deliberately the team-management endpoint rather than a
   * second copy on this controller — that one already owns the cross-job guard and stamps the
   * team onto the audit row, and two send paths would be two places to drift.
   */
  sendTeamPush(teamId: string, pushText: string): Observable<TeamPushDto> {
    return this.http.post<TeamPushDto>(
      `${environment.apiUrl}/teams/${teamId}/pushes`,
      { pushText, addAllTeams: false });
  }

  getHistory(): Observable<PushNotificationHistoryDto[]> {
    return this.http.get<PushNotificationHistoryDto[]>(`${this.apiUrl}/history`);
  }
}
