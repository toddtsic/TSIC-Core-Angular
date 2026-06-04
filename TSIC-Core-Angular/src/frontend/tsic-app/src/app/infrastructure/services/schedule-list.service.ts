import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ScheduleListFieldDto, ScheduleListRequestDto } from '@core/api';

/**
 * Schedule List Designer API — fetches the configurable field pool and posts a render
 * config to generate the PDF. Blob download is delegated to ReportingService.triggerDownload.
 */
@Injectable({ providedIn: 'root' })
export class ScheduleListService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /** The columns the Designer can place, with default width/align. */
    getFields(): Observable<ScheduleListFieldDto[]> {
        return this.http.get<ScheduleListFieldDto[]>(`${this.apiUrl}/schedule-list/fields`);
    }

    /** Renders the schedule-list PDF for the caller's current job from the given config. */
    generate(request: ScheduleListRequestDto): Observable<HttpResponse<Blob>> {
        return this.http.post(`${this.apiUrl}/schedule-list/generate`, request, {
            responseType: 'blob',
            observe: 'response',
        });
    }
}
