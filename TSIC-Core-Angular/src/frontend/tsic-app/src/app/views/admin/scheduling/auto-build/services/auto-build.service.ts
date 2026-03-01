import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AutoBuildRequest,
    AutoBuildResult,
    DivisionStrategyEntry,
    DivisionStrategyProfileResponse,
    EnsurePairingsRequest,
    EnsurePairingsResponse,
    GameSummaryResponse,
    PrerequisiteCheckResponse,
    ProfileExtractionResponse,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class AutoBuildService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/auto-build`;

    getGameSummary(): Observable<GameSummaryResponse> {
        return this.http.get<GameSummaryResponse>(`${this.apiUrl}/game-summary`);
    }

    undo(): Observable<{ gamesDeleted: number }> {
        return this.http.post<{ gamesDeleted: number }>(
            `${this.apiUrl}/undo`,
            {}
        );
    }

    checkPrerequisites(): Observable<PrerequisiteCheckResponse> {
        return this.http.get<PrerequisiteCheckResponse>(
            `${this.apiUrl}/prerequisites`
        );
    }

    extractProfiles(sourceJobId: string): Observable<ProfileExtractionResponse> {
        return this.http.post<ProfileExtractionResponse>(
            `${this.apiUrl}/extract-profiles`,
            { sourceJobId }
        );
    }

    execute(request: AutoBuildRequest): Observable<AutoBuildResult> {
        return this.http.post<AutoBuildResult>(
            `${this.apiUrl}/execute`,
            request
        );
    }

    getStrategyProfiles(sourceJobId?: string): Observable<DivisionStrategyProfileResponse> {
        const params = sourceJobId ? `?sourceJobId=${sourceJobId}` : '';
        return this.http.get<DivisionStrategyProfileResponse>(
            `${this.apiUrl}/strategy-profiles${params}`
        );
    }

    saveStrategyProfiles(strategies: DivisionStrategyEntry[]): Observable<DivisionStrategyProfileResponse> {
        return this.http.put<DivisionStrategyProfileResponse>(
            `${this.apiUrl}/strategy-profiles`,
            strategies
        );
    }

    ensurePairings(request: EnsurePairingsRequest): Observable<EnsurePairingsResponse> {
        return this.http.post<EnsurePairingsResponse>(
            `${this.apiUrl}/ensure-pairings`,
            request
        );
    }

    /** Dev-only: clear all scheduling config (games, timeslots, pairings, fields, profiles). */
    devReset(): Observable<{ gamesDeleted: number; agegroupsCleared: number; pairingGroupsCleared: number; fieldsCleared: number }> {
        return this.http.post<{ gamesDeleted: number; agegroupsCleared: number; pairingGroupsCleared: number; fieldsCleared: number }>(
            `${environment.apiUrl}/dev-scheduling/reset`,
            {}
        );
    }
}
