import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

// ── Response shapes (controller returns anonymous objects — not auto-generated) ──

export interface LogEntryDto {
	id: number;
	timeStamp: string;
	level: string;
	message: string;
	exception: string | null;
	sourceContext: string | null;
	requestPath: string | null;
	statusCode: number | null;
	elapsed: number | null;
}

export interface LogPageResult {
	items: LogEntryDto[];
	totalCount: number;
	page: number;
	pageSize: number;
}

export interface LogCountByHour {
	hour: string;
	count: number;
	level: string;
}

export interface LogCountByHourByStatus {
	hour: string;
	count: number;
	statusRange: string;
}

export interface TopErrorDto {
	message: string;
	count: number;
	lastSeen: string;
}

export interface LogStatsDto {
	countsByHour: LogCountByHour[];
	countsByHourByStatus: LogCountByHourByStatus[];
	countsByLevel: Record<string, number>;
	topErrors: TopErrorDto[];
	totalCount: number;
}

export interface LogQueryParams {
	level?: string;
	search?: string;
	from?: string;
	to?: string;
	page?: number;
	pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class LogViewerService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/admin/logs`;

	query(params: LogQueryParams): Observable<LogPageResult> {
		let httpParams = new HttpParams();
		if (params.level) httpParams = httpParams.set('level', params.level);
		if (params.search) httpParams = httpParams.set('search', params.search);
		if (params.from) httpParams = httpParams.set('from', params.from);
		if (params.to) httpParams = httpParams.set('to', params.to);
		if (params.page) httpParams = httpParams.set('page', params.page.toString());
		if (params.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());
		return this.http.get<LogPageResult>(this.apiUrl, { params: httpParams });
	}

	getStats(from: string, to: string): Observable<LogStatsDto> {
		const params = new HttpParams().set('from', from).set('to', to);
		return this.http.get<LogStatsDto>(`${this.apiUrl}/stats`, { params });
	}

	purge(daysToKeep: number): Observable<{ deletedCount: number; cutoffDate: string }> {
		const params = new HttpParams().set('daysToKeep', daysToKeep.toString());
		return this.http.delete<{ deletedCount: number; cutoffDate: string }>(
			`${this.apiUrl}/purge`, { params }
		);
	}
}
