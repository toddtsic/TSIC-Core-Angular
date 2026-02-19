import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	JobTypeRefDto,
	RoleRefDto,
	WidgetCategoryRefDto,
	WidgetDefinitionDto,
	CreateWidgetRequest,
	UpdateWidgetRequest,
	WidgetDefaultMatrixResponse,
	SaveWidgetDefaultsRequest,
	WidgetAssignmentsResponse,
	SaveWidgetAssignmentsRequest,
	JobRefDto,
	JobOverridesResponse,
	SaveJobOverridesRequest,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class WidgetEditorService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/widget-editor`;

	// ── Reference data ──

	getJobTypes(): Observable<JobTypeRefDto[]> {
		return this.http.get<JobTypeRefDto[]>(`${this.apiUrl}/job-types`);
	}

	getRoles(): Observable<RoleRefDto[]> {
		return this.http.get<RoleRefDto[]>(`${this.apiUrl}/roles`);
	}

	getCategories(): Observable<WidgetCategoryRefDto[]> {
		return this.http.get<WidgetCategoryRefDto[]>(`${this.apiUrl}/categories`);
	}

	// ── Widget definitions ──

	getWidgets(): Observable<WidgetDefinitionDto[]> {
		return this.http.get<WidgetDefinitionDto[]>(`${this.apiUrl}/widgets`);
	}

	createWidget(request: CreateWidgetRequest): Observable<WidgetDefinitionDto> {
		return this.http.post<WidgetDefinitionDto>(`${this.apiUrl}/widgets`, request);
	}

	updateWidget(widgetId: number, request: UpdateWidgetRequest): Observable<WidgetDefinitionDto> {
		return this.http.put<WidgetDefinitionDto>(`${this.apiUrl}/widgets/${widgetId}`, request);
	}

	deleteWidget(widgetId: number): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/widgets/${widgetId}`);
	}

	// ── Defaults matrix ──

	getDefaultsMatrix(jobTypeId: number): Observable<WidgetDefaultMatrixResponse> {
		return this.http.get<WidgetDefaultMatrixResponse>(`${this.apiUrl}/defaults/${jobTypeId}`);
	}

	saveDefaultsMatrix(request: SaveWidgetDefaultsRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/defaults/${request.jobTypeId}`, request);
	}

	// ── Widget-centric assignments ──

	getWidgetAssignments(widgetId: number): Observable<WidgetAssignmentsResponse> {
		return this.http.get<WidgetAssignmentsResponse>(`${this.apiUrl}/widgets/${widgetId}/assignments`);
	}

	saveWidgetAssignments(request: SaveWidgetAssignmentsRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/widgets/${request.widgetId}/assignments`, request);
	}

	// ── Per-job overrides ──

	getJobsByJobType(jobTypeId: number): Observable<JobRefDto[]> {
		return this.http.get<JobRefDto[]>(`${this.apiUrl}/jobs-by-type/${jobTypeId}`);
	}

	getJobOverrides(jobId: string): Observable<JobOverridesResponse> {
		return this.http.get<JobOverridesResponse>(`${this.apiUrl}/job-overrides/${jobId}`);
	}

	saveJobOverrides(request: SaveJobOverridesRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/job-overrides/${request.jobId}`, request);
	}

	// ── Export SQL ──

	exportSql(): Observable<string> {
		return this.http.get<{ sql: string }>(`${this.apiUrl}/export-sql`).pipe(
			map(res => res.sql)
		);
	}

	// ── Seed script sync ──

	syncSeedScript(): Observable<SeedScriptSyncResult> {
		return this.http.post<SeedScriptSyncResult>(`${this.apiUrl}/sync-seed-script`, {});
	}
}

export interface SeedScriptSyncResult {
	message: string;
	filePath: string;
	categoriesCount: number;
	widgetsCount: number;
	defaultsCount: number;
	jobWidgetsCount: number;
}
