import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AutoBuildSourceJobDto,
    AutoBuildAnalysisResponse,
    AutoBuildRequest,
    AutoBuildResult,
    GameSummaryResponse,
    AgegroupMappingResponse,
    ConfirmedAgegroupMapping,
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

    proposeMappings(sourceJobId: string): Observable<AgegroupMappingResponse> {
        return this.http.post<AgegroupMappingResponse>(
            `${this.apiUrl}/propose-mappings`,
            { sourceJobId }
        );
    }

    analyze(sourceJobId: string, agegroupMappings?: ConfirmedAgegroupMapping[]): Observable<AutoBuildAnalysisResponse> {
        return this.http.post<AutoBuildAnalysisResponse>(
            `${this.apiUrl}/analyze`,
            { sourceJobId, agegroupMappings }
        );
    }

    execute(request: AutoBuildRequest): Observable<AutoBuildResult> {
        return this.http.post<AutoBuildResult>(
            `${this.apiUrl}/execute`,
            request
        );
    }

    undo(): Observable<{ gamesDeleted: number }> {
        return this.http.post<{ gamesDeleted: number }>(
            `${this.apiUrl}/undo`,
            {}
        );
    }
}
