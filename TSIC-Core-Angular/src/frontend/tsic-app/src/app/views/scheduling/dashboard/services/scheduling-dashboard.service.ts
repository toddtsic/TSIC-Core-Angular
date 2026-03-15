import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { SchedulingDashboardStatusDto } from '@core/api';

export type { SchedulingDashboardStatusDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class SchedulingDashboardService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/scheduling-dashboard`;

    getStatus(): Observable<SchedulingDashboardStatusDto> {
        return this.http.get<SchedulingDashboardStatusDto>(`${this.apiUrl}/status`);
    }
}
