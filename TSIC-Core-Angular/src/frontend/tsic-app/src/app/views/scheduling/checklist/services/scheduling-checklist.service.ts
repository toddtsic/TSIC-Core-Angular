import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ScheduleDashboardDto, SchedulingChecklistDto } from '@core/api';

export type { ScheduleDashboardDto, SchedulingChecklistDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class SchedulingChecklistService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/scheduling-checklist`;

    getChecklist(): Observable<SchedulingChecklistDto> {
        return this.http.get<SchedulingChecklistDto>(this.apiUrl);
    }

    getDashboard(): Observable<ScheduleDashboardDto> {
        return this.http.get<ScheduleDashboardDto>(`${this.apiUrl}/dashboard`);
    }
}
