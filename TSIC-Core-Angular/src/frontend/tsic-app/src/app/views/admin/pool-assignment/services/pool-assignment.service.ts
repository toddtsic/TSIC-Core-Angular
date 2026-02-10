import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    PoolDivisionOptionDto,
    PoolTeamDto,
    PoolTransferPreviewRequest,
    PoolTransferPreviewResponse,
    PoolTransferRequest,
    PoolTransferResultDto
} from '@core/api';

// Re-export for consumers
export type {
    PoolDivisionOptionDto,
    PoolTeamDto,
    PoolTransferPreviewResponse,
    PoolTransferResultDto,
    PoolTransferPreviewDto,
    PoolClubRepImpactDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class PoolAssignmentService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/pool-assignment`;

    getDivisions(): Observable<PoolDivisionOptionDto[]> {
        return this.http.get<PoolDivisionOptionDto[]>(`${this.apiUrl}/divisions`);
    }

    getTeams(divId: string): Observable<PoolTeamDto[]> {
        return this.http.get<PoolTeamDto[]>(`${this.apiUrl}/divisions/${divId}/teams`);
    }

    previewTransfer(request: PoolTransferPreviewRequest): Observable<PoolTransferPreviewResponse> {
        return this.http.post<PoolTransferPreviewResponse>(`${this.apiUrl}/preview`, request);
    }

    executeTransfer(request: PoolTransferRequest): Observable<PoolTransferResultDto> {
        return this.http.post<PoolTransferResultDto>(`${this.apiUrl}/transfer`, request);
    }

    toggleTeamActive(teamId: string, active: boolean): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/teams/${teamId}/active`, { active });
    }

    updateTeamDivRank(teamId: string, divRank: number): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/teams/${teamId}/divrank`, { divRank });
    }
}
