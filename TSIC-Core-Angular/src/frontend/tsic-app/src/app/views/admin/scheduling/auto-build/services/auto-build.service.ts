import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AutoBuildSourceJobDto } from '@core/api';
import type { AutoBuildAnalysisResponse } from '@core/api';
import type { AutoBuildRequest } from '@core/api';
import type { AutoBuildResult } from '@core/api';

@Injectable({ providedIn: 'root' })
export class AutoBuildService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/auto-build`;

    getSourceJobs(): Observable<AutoBuildSourceJobDto[]> {
        return this.http.get<AutoBuildSourceJobDto[]>(`${this.apiUrl}/source-jobs`);
    }

    analyze(sourceJobId: string): Observable<AutoBuildAnalysisResponse> {
        return this.http.post<AutoBuildAnalysisResponse>(
            `${this.apiUrl}/analyze`,
            { sourceJobId }
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
