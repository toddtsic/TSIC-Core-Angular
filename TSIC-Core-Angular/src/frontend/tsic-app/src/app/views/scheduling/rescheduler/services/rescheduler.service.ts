import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    ScheduleFilterOptionsDto,
    ScheduleGridResponse,
    ReschedulerGridRequest,
    MoveGameRequest,
    AffectedGameCountResponse,
    AdjustWeatherRequest,
    AdjustWeatherResponse,
    EmailRecipientCountResponse,
    EmailParticipantsRequest,
    EmailParticipantsResponse
} from '@core/api';

// Re-export for consumers
export type {
    ScheduleFilterOptionsDto,
    ScheduleGridResponse,
    ScheduleGridRow,
    ScheduleFieldColumn,
    ScheduleGameDto,
    ReschedulerGridRequest,
    MoveGameRequest,
    AffectedGameCountResponse,
    AdjustWeatherRequest,
    AdjustWeatherResponse,
    EmailRecipientCountResponse,
    EmailParticipantsRequest,
    EmailParticipantsResponse,
    CadtClubNode,
    CadtAgegroupNode,
    CadtDivisionNode,
    CadtTeamNode,
    FieldSummaryDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class ReschedulerService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/Rescheduler`;

    // ── Filter Options ──

    getFilterOptions(): Observable<ScheduleFilterOptionsDto> {
        return this.http.get<ScheduleFilterOptionsDto>(`${this.apiUrl}/filter-options`);
    }

    // ── Grid ──

    getGrid(request: ReschedulerGridRequest): Observable<ScheduleGridResponse> {
        return this.http.post<ScheduleGridResponse>(`${this.apiUrl}/grid`, request);
    }

    // ── Move/Swap ──

    moveGame(request: MoveGameRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/move-game`, request);
    }

    // ── Weather Adjustment ──

    getAffectedCount(preFirstGame: string, fieldIds: string[]): Observable<AffectedGameCountResponse> {
        let params = new HttpParams().set('preFirstGame', preFirstGame);
        for (const fid of fieldIds) {
            params = params.append('fieldIds', fid);
        }
        return this.http.get<AffectedGameCountResponse>(`${this.apiUrl}/affected-count`, { params });
    }

    adjustWeather(request: AdjustWeatherRequest): Observable<AdjustWeatherResponse> {
        return this.http.post<AdjustWeatherResponse>(`${this.apiUrl}/adjust-weather`, request);
    }

    // ── Email ──

    getRecipientCount(firstGame: string, lastGame: string, fieldIds: string[]): Observable<EmailRecipientCountResponse> {
        let params = new HttpParams()
            .set('firstGame', firstGame)
            .set('lastGame', lastGame);
        for (const fid of fieldIds) {
            params = params.append('fieldIds', fid);
        }
        return this.http.get<EmailRecipientCountResponse>(`${this.apiUrl}/recipient-count`, { params });
    }

    emailParticipants(request: EmailParticipantsRequest): Observable<EmailParticipantsResponse> {
        return this.http.post<EmailParticipantsResponse>(`${this.apiUrl}/email-participants`, request);
    }
}
