import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  PushNotificationDeviceCountDto,
  PushNotificationHistoryDto,
  SendPushNotificationResponse
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/push-notifications`;

  getDeviceCount(): Observable<PushNotificationDeviceCountDto> {
    return this.http.get<PushNotificationDeviceCountDto>(`${this.apiUrl}/device-count`);
  }

  sendPush(pushText: string): Observable<SendPushNotificationResponse> {
    return this.http.post<SendPushNotificationResponse>(`${this.apiUrl}/send`, { pushText });
  }

  getHistory(): Observable<PushNotificationHistoryDto[]> {
    return this.http.get<PushNotificationHistoryDto[]>(`${this.apiUrl}/history`);
  }
}
