import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	BlankJobRequest,
	BlankJobResponse,
	DevUndoStatusResponse,
	IdentityExistsResponse,
	JobClonePreviewResponse,
	JobCloneRequest,
	JobCloneResponse,
	JobCloneSourceDto,
	ReleasableAdminDto,
	ReleaseAdminsRequest,
	ReleaseResponse,
	SuspendedJobDto,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobCloneService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/job-clone`;

	// ── Source picker (existing) ──
	getSources(): Observable<JobCloneSourceDto[]> {
		return this.http.get<JobCloneSourceDto[]>(`${this.apiUrl}/sources`);
	}

	// ── Clone flow ──
	cloneJob(request: JobCloneRequest): Observable<JobCloneResponse> {
		return this.http.post<JobCloneResponse>(this.apiUrl, request);
	}

	previewClone(request: JobCloneRequest): Observable<JobClonePreviewResponse> {
		return this.http.post<JobClonePreviewResponse>(`${this.apiUrl}/preview`, request);
	}

	jobIdentityExists(path: string, name: string): Observable<IdentityExistsResponse> {
		const params = new HttpParams().set('path', path).set('name', name);
		return this.http.get<IdentityExistsResponse>(`${this.apiUrl}/identity-exists`, { params });
	}

	// ── Blank flow ──
	createBlank(request: BlankJobRequest): Observable<BlankJobResponse> {
		return this.http.post<BlankJobResponse>(`${this.apiUrl}/blank`, request);
	}

	// ── Release flow ──
	getSuspended(): Observable<SuspendedJobDto[]> {
		return this.http.get<SuspendedJobDto[]>(`${this.apiUrl}/suspended`);
	}

	getAdmins(jobId: string): Observable<ReleasableAdminDto[]> {
		return this.http.get<ReleasableAdminDto[]>(`${this.apiUrl}/${jobId}/admins`);
	}

	releaseSite(jobId: string): Observable<ReleaseResponse> {
		return this.http.post<ReleaseResponse>(`${this.apiUrl}/${jobId}/release-site`, {});
	}

	releaseAdmins(jobId: string, request: ReleaseAdminsRequest): Observable<ReleaseResponse> {
		return this.http.post<ReleaseResponse>(`${this.apiUrl}/${jobId}/release-admins`, request);
	}

	// ── Dev-only undo (404 in prod) ──
	getDevUndoStatus(jobId: string): Observable<DevUndoStatusResponse> {
		return this.http.get<DevUndoStatusResponse>(`${this.apiUrl}/${jobId}/dev-undo-status`);
	}

	deleteClonedJob(jobId: string): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/${jobId}/dev-undo`);
	}
}
