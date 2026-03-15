import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AgegroupWithDivisionsDto,
    DivisionPairingsResponse,
    WhoPlaysWhoResponse,
    PairingDto,
    AddPairingBlockRequest,
    AddSingleEliminationRequest,
    AddSinglePairingRequest,
    EditPairingRequest,
    RemoveAllPairingsRequest,
    DivisionTeamDto,
    EditDivisionTeamRequest
} from '@core/api';

// Re-export for consumers
export type {
    AgegroupWithDivisionsDto,
    DivisionSummaryDto,
    DivisionPairingsResponse,
    WhoPlaysWhoResponse,
    PairingDto,
    AddPairingBlockRequest,
    AddSingleEliminationRequest,
    AddSinglePairingRequest,
    EditPairingRequest,
    RemoveAllPairingsRequest,
    DivisionTeamDto,
    EditDivisionTeamRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class PairingsService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/pairings`;

    getAgegroups(): Observable<AgegroupWithDivisionsDto[]> {
        return this.http.get<AgegroupWithDivisionsDto[]>(`${this.apiUrl}/agegroups`);
    }

    getDivisionPairings(divId: string): Observable<DivisionPairingsResponse> {
        return this.http.get<DivisionPairingsResponse>(`${this.apiUrl}/division/${divId}`);
    }

    getWhoPlaysWho(teamCount: number): Observable<WhoPlaysWhoResponse> {
        return this.http.get<WhoPlaysWhoResponse>(`${this.apiUrl}/who-plays-who`, {
            params: { teamCount: teamCount.toString() }
        });
    }

    addBlock(request: AddPairingBlockRequest): Observable<PairingDto[]> {
        return this.http.post<PairingDto[]>(`${this.apiUrl}/add-block`, request);
    }

    addElimination(request: AddSingleEliminationRequest): Observable<PairingDto[]> {
        return this.http.post<PairingDto[]>(`${this.apiUrl}/add-elimination`, request);
    }

    addSingle(request: AddSinglePairingRequest): Observable<PairingDto> {
        return this.http.post<PairingDto>(`${this.apiUrl}/add-single`, request);
    }

    editPairing(request: EditPairingRequest): Observable<void> {
        return this.http.put<void>(this.apiUrl, request);
    }

    deletePairing(ai: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${ai}`);
    }

    removeAll(request: RemoveAllPairingsRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/remove-all`, request);
    }

    // ── Division Teams ──

    getDivisionTeams(divId: string): Observable<DivisionTeamDto[]> {
        return this.http.get<DivisionTeamDto[]>(`${this.apiUrl}/division/${divId}/teams`);
    }

    editDivisionTeam(request: EditDivisionTeamRequest): Observable<DivisionTeamDto[]> {
        return this.http.put<DivisionTeamDto[]>(`${this.apiUrl}/division-team`, request);
    }
}
