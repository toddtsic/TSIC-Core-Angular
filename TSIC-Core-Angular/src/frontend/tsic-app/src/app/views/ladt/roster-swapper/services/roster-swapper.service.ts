import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    SwapperPoolOptionDto,
    SwapperPlayerDto,
    RosterTransferPreviewRequest,
    RosterTransferRequest,
    RosterTransferFeePreviewDto,
    RosterTransferResultDto,
    UnassignedAdultQueueRowDto
} from '@core/api';

// Re-export for consumers
export type { SwapperPoolOptionDto, SwapperPlayerDto, RosterTransferFeePreviewDto, RosterTransferResultDto, UnassignedAdultQueueRowDto };

@Injectable({ providedIn: 'root' })
export class RosterSwapperService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/roster-swapper`;

    getPoolOptions(): Observable<SwapperPoolOptionDto[]> {
        return this.http.get<SwapperPoolOptionDto[]>(`${this.apiUrl}/pools`);
    }

    getRoster(poolId: string): Observable<SwapperPlayerDto[]> {
        return this.http.get<SwapperPlayerDto[]>(`${this.apiUrl}/roster/${poolId}`);
    }

    previewTransfer(request: RosterTransferPreviewRequest): Observable<RosterTransferFeePreviewDto[]> {
        return this.http.post<RosterTransferFeePreviewDto[]>(`${this.apiUrl}/preview`, request);
    }

    executeTransfer(request: RosterTransferRequest): Observable<RosterTransferResultDto> {
        return this.http.post<RosterTransferResultDto>(`${this.apiUrl}/transfer`, request);
    }

    togglePlayerActive(registrationId: string, active: boolean): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/players/${registrationId}/active`, { bActive: active });
    }

    /** Director approval queue: unassigned coaches with pending team requests + recognition context. */
    getUnassignedQueue(): Observable<UnassignedAdultQueueRowDto[]> {
        return this.http.get<UnassignedAdultQueueRowDto[]>(`${this.apiUrl}/unassigned-queue`);
    }

    /** Approve one (coach, team) request — mints the per-team Staff row via FLOW 2. */
    approveRequest(registrationId: string, teamId: string): Observable<RosterTransferResultDto> {
        return this.http.post<RosterTransferResultDto>(`${this.apiUrl}/approve-request`, { registrationId, teamId });
    }

    /** Deny one (coach, team) request — drops it from the coach's codified requests. */
    denyRequest(registrationId: string, teamId: string): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/deny-request`, { registrationId, teamId });
    }
}
