import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { PackedRosterFieldDto, PackedRosterRequestDto } from '@core/api';

/**
 * PackedRoster Designer API — fetches the configurable field pool and posts a render
 * config to generate the PDF. Blob download is delegated to ReportingService.triggerDownload.
 */
@Injectable({ providedIn: 'root' })
export class PackedRosterService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /** The player-row columns the Designer can place, with default width/align. */
    getFields(): Observable<PackedRosterFieldDto[]> {
        return this.http.get<PackedRosterFieldDto[]>(`${this.apiUrl}/packed-roster/fields`);
    }

    /** Renders the packed-roster PDF for the caller's current job from the given config. */
    generate(request: PackedRosterRequestDto): Observable<HttpResponse<Blob>> {
        return this.http.post(`${this.apiUrl}/packed-roster/generate`, request, {
            responseType: 'blob',
            observe: 'response',
        });
    }

    /**
     * Renders the recruiter report (player-as-card) PDF for the caller's current job —
     * reproduces the legacy LFTC Recruiters report off the same EF roster query. Fixed
     * layout, so no config: a plain GET, jobId derived server-side from JWT claims.
     */
    generateRecruiter(): Observable<HttpResponse<Blob>> {
        return this.http.get(`${this.apiUrl}/packed-roster/recruiter`, {
            responseType: 'blob',
            observe: 'response',
        });
    }
}
