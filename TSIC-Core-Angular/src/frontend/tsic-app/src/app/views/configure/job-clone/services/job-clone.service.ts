import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	BlankJobRequest,
	BlankJobResponse,
	IdentityExistsResponse,
	JobClonePreviewResponse,
	JobCloneRequest,
	JobCloneResponse,
	JobCloneSourceDto,
	ReleasableAdminDto,
	ReleaseAdminsRequest,
	ReleaseResponse,
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
	getAdmins(jobId: string): Observable<ReleasableAdminDto[]> {
		return this.http.get<ReleasableAdminDto[]>(`${this.apiUrl}/${jobId}/admins`);
	}

	releaseSite(jobId: string): Observable<ReleaseResponse> {
		return this.http.post<ReleaseResponse>(`${this.apiUrl}/${jobId}/release-site`, {});
	}

	releaseAdmins(jobId: string, request: ReleaseAdminsRequest): Observable<ReleaseResponse> {
		return this.http.post<ReleaseResponse>(`${this.apiUrl}/${jobId}/release-admins`, request);
	}
}
