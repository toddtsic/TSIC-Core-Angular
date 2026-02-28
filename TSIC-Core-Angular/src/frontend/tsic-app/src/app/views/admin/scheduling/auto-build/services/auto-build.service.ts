import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AutoBuildSourceJobDto,
    AutoBuildV2Request,
    AutoBuildV2Result,
    GameSummaryResponse,
    PrerequisiteCheckResponse,
    ProfileExtractionResponse,
    DivisionStrategyProfileResponse,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class AutoBuildService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/auto-build`;

    getGameSummary(): Observable<GameSummaryResponse> {
        return this.http.get<GameSummaryResponse>(`${this.apiUrl}/game-summary`);
    }

    getSourceJobs(): Observable<AutoBuildSourceJobDto[]> {
        return this.http.get<AutoBuildSourceJobDto[]>(`${this.apiUrl}/source-jobs`);
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

    executeV2(request: AutoBuildV2Request): Observable<AutoBuildV2Result> {
        return this.http.post<AutoBuildV2Result>(
            `${this.apiUrl}/execute-v2`,
            request
        );
    }

    getStrategyProfiles(sourceJobId?: string): Observable<DivisionStrategyProfileResponse> {
        const params = sourceJobId ? `?sourceJobId=${sourceJobId}` : '';
        return this.http.get<DivisionStrategyProfileResponse>(
            `${this.apiUrl}/strategy-profiles${params}`
        );
    }
}
