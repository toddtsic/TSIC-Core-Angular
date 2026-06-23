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
    UnassignedAdultQueueRowDto,
    UnassignedAdultAssignedTeamDto,
    UnassignedAdultRecordedTeamDto
} from '@core/api';

// Re-export for consumers
export type { SwapperPoolOptionDto, SwapperPlayerDto, RosterTransferFeePreviewDto, RosterTransferResultDto, UnassignedAdultQueueRowDto, UnassignedAdultAssignedTeamDto, UnassignedAdultRecordedTeamDto };

/** All-zeros GUID = the Unassigned Adults pool (transfer source/target sentinel). */
const UNASSIGNED_POOL_ID = '00000000-0000-0000-0000-000000000000';

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

    /** Deny a coach outright — deletes ALL their Staff rows + deactivates the anchor (bActive=0). */
    denyCoach(registrationId: string): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/deny-coach`, { registrationId });
    }

    /**
     * Remove a coach from a team — deletes their Staff registration (Roster Swapper FLOW 3:
     * Staff → Unassigned pool). `staffRegistrationId` is the Staff row on `teamId`.
     */
    removeStaffFromTeam(staffRegistrationId: string, teamId: string): Observable<RosterTransferResultDto> {
        return this.executeTransfer({
            registrationIds: [staffRegistrationId],
            sourcePoolId: teamId,
            targetPoolId: UNASSIGNED_POOL_ID
        });
    }
}
