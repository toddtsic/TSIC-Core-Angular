import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AgegroupDistributionDto, AvailableWidgetDto, DashboardMetricsDto, EventContactDto, RegistrationTimeSeriesDto, SaveUserWidgetsRequest, UserWidgetEntryDto, WidgetDashboardResponse, YearOverYearComparisonDto } from '@core/api';

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

	getMetrics(): Observable<DashboardMetricsDto> {
		return this.http.get<DashboardMetricsDto>(`${this.apiUrl}/metrics`);
	}

	getRegistrationTrend(): Observable<RegistrationTimeSeriesDto> {
		return this.http.get<RegistrationTimeSeriesDto>(`${this.apiUrl}/registration-trend`);
	}

	getPlayerTrend(): Observable<RegistrationTimeSeriesDto> {
		return this.http.get<RegistrationTimeSeriesDto>(`${this.apiUrl}/player-trend`);
	}

	getTeamTrend(): Observable<RegistrationTimeSeriesDto> {
		return this.http.get<RegistrationTimeSeriesDto>(`${this.apiUrl}/team-trend`);
	}

	getAgegroupDistribution(): Observable<AgegroupDistributionDto> {
		return this.http.get<AgegroupDistributionDto>(`${this.apiUrl}/agegroup-distribution`);
	}

	getEventContact(): Observable<EventContactDto> {
		return this.http.get<EventContactDto>(`${this.apiUrl}/event-contact`);
	}

	getPublicEventContact(jobPath: string): Observable<EventContactDto> {
		return this.http.get<EventContactDto>(`${this.apiUrl}/public/${jobPath}/event-contact`);
	}

	getYearOverYear(): Observable<YearOverYearComparisonDto> {
		return this.http.get<YearOverYearComparisonDto>(`${this.apiUrl}/year-over-year`);
	}

	// ── User Widget Customization ──

	getAvailableWidgets(): Observable<AvailableWidgetDto[]> {
		return this.http.get<AvailableWidgetDto[]>(`${this.apiUrl}/available-widgets`);
	}

	getMyWidgets(): Observable<UserWidgetEntryDto[]> {
		return this.http.get<UserWidgetEntryDto[]>(`${this.apiUrl}/my-widgets`);
	}

	saveMyWidgets(request: SaveUserWidgetsRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/my-widgets`, request);
	}

	resetMyWidgets(): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/my-widgets`);
	}
}
