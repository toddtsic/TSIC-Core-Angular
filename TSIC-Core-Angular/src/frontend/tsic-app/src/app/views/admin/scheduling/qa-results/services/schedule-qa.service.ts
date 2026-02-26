import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AutoBuildQaResult } from '@core/api';

@Injectable({ providedIn: 'root' })
export class ScheduleQaService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/schedule-qa`;

    validate(): Observable<AutoBuildQaResult> {
        return this.http.get<AutoBuildQaResult>(`${this.apiUrl}/validate`);
    }

    exportExcel(): Observable<HttpResponse<Blob>> {
        return this.http.get(`${this.apiUrl}/export-excel`, {
            responseType: 'blob',
            observe: 'response'
        });
    }
}
