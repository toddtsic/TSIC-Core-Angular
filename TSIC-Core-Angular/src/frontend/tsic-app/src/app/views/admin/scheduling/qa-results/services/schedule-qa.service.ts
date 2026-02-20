import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
}
