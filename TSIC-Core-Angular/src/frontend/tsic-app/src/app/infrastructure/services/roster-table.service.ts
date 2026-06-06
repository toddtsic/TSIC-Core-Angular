import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { RosterTableFieldDto, RosterTableRequestDto } from '@core/api';

/**
 * Roster Table Designer API — fetches the configurable field pool and posts a render
 * config to generate the PDF. Blob download is delegated to ReportingService.triggerDownload.
 * Sibling of ScheduleListService (same field-pool + generate shape).
 */
@Injectable({ providedIn: 'root' })
export class RosterTableService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /** The columns the Designer can place, with default width/align. */
    getFields(): Observable<RosterTableFieldDto[]> {
        return this.http.get<RosterTableFieldDto[]>(`${this.apiUrl}/roster-table/fields`);
    }

    /** Renders the roster-table PDF for the caller's current job from the given config. */
    generate(request: RosterTableRequestDto): Observable<HttpResponse<Blob>> {
        return this.http.post(`${this.apiUrl}/roster-table/generate`, request, {
            responseType: 'blob',
            observe: 'response',
        });
    }
}
