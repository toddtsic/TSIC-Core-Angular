import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type { WidgetDashboardResponse } from '@core/api';

@Injectable({ providedIn: 'root' })
export class WidgetDashboardService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/widget-dashboard`;

	getDashboard(): Observable<WidgetDashboardResponse> {
		return this.http.get<WidgetDashboardResponse>(this.apiUrl);
	}

	getPublicDashboard(jobPath: string): Observable<WidgetDashboardResponse> {
		return this.http.get<WidgetDashboardResponse>(`${this.apiUrl}/public/${jobPath}`);
	}
}
