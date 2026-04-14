import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { JobFilterTreeDto } from '@core/api';

/**
 * Single source of truth for the unified CADT/LADT filter tree.
 * Backed by GET /api/job-filter-tree — returns both trees with rich metadata
 * flags so each consumer filters per its context (requireScheduled,
 * requireClubRep, excludeWaitlistDropped) client-side.
 */
@Injectable({ providedIn: 'root' })
export class JobFilterTreeService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/job-filter-tree`;

    getForJob(jobPath?: string): Observable<JobFilterTreeDto> {
        if (jobPath) {
            return this.http.get<JobFilterTreeDto>(this.apiUrl, { params: { jobPath } });
        }
        return this.http.get<JobFilterTreeDto>(this.apiUrl);
    }
}
