import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	JobTypeRefDto,
	JobRefDto,
	JobPulseDto,
	QuickLinkEditorModelDto,
	SaveQuickLinksRequest,
} from '@core/api';

/**
 * HTTP access for the SuperUser QuickLinks editor. The job-type → job picker
 * mirrors the widget-editor; the pulse fetch drives the read-only grounded
 * on/off preview (same source the public landing hero resolves from).
 */
@Injectable({ providedIn: 'root' })
export class QuicklinksEditorService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/quicklinks`;

	getJobTypes(): Observable<JobTypeRefDto[]> {
		return this.http.get<JobTypeRefDto[]>(`${this.apiUrl}/job-types`);
	}

	getJobsByJobType(jobTypeId: number): Observable<JobRefDto[]> {
		return this.http.get<JobRefDto[]>(`${this.apiUrl}/jobs-by-type/${jobTypeId}`);
	}

	getEditorModel(jobId: string): Observable<QuickLinkEditorModelDto> {
		return this.http.get<QuickLinkEditorModelDto>(`${this.apiUrl}/editor/${jobId}`);
	}

	save(jobId: string, request: SaveQuickLinksRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/editor/${jobId}`, request);
	}

	/** The chosen job's pulse — used to preview grounded links' derived on/off. */
	getPulse(jobPath: string): Observable<JobPulseDto> {
		return this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`);
	}
}
