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
  SendTeamsPushResponse
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
   * One push to a chosen set of teams. A device following more than one of them receives a
   * single notification; the backend writes one audit row per team.
   *
   * Its own endpoint rather than a loop over the single-team one: N requests would be N audit
   * transactions and would buzz a parent's phone once per child's team.
   */
  sendTeamsPush(teamIds: string[], pushText: string): Observable<SendTeamsPushResponse> {
    return this.http.post<SendTeamsPushResponse>(
      `${this.apiUrl}/send-teams`, { pushText, teamIds });
  }

  getHistory(): Observable<PushNotificationHistoryDto[]> {
    return this.http.get<PushNotificationHistoryDto[]>(`${this.apiUrl}/history`);
  }
}
