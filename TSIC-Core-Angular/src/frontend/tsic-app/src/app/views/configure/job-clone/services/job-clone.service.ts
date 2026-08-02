import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	ClonePlanDto,
	DevUndoStatusResponse,
	IdentityExistsResponse,
	JobCloneRequest,
	JobCloneResponse,
	JobCloneSourceDto,
	JobConfigReferenceDataDto,
	JobVerifyChecklistDto,
	OpenRegistrationRequest,
	RegistrationFlagsDto,
	ReleasableAdminDto,
	ReleaseAdminsRequest,
	ReleaseResponse,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobCloneService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/job-clone`;

	// ── Source picker ──
	getSources(): Observable<JobCloneSourceDto[]> {
		return this.http.get<JobCloneSourceDto[]>(`${this.apiUrl}/sources`);
	}

	// ── Clone flow (one plan, two consumers) ──
	previewClone(request: JobCloneRequest): Observable<ClonePlanDto> {
		return this.http.post<ClonePlanDto>(`${this.apiUrl}/preview`, request);
	}

	cloneJob(request: JobCloneRequest): Observable<JobCloneResponse> {
		return this.http.post<JobCloneResponse>(this.apiUrl, request);
	}

	jobIdentityExists(path: string, name: string): Observable<IdentityExistsResponse> {
		const params = new HttpParams().set('path', path).set('name', name);
		return this.http.get<IdentityExistsResponse>(`${this.apiUrl}/identity-exists`, { params });
	}

	/** Customers (plus sports / job types / billing types) — feeds the owner picker. */
	getReferenceData(): Observable<JobConfigReferenceDataDto> {
		return this.http.get<JobConfigReferenceDataDto>(`${environment.apiUrl}/job-config/reference-data`);
	}

	// ── Release flow (verify → site → admins → open registration) ──
	getVerifyChecklist(jobId: string): Observable<JobVerifyChecklistDto> {
		return this.http.get<JobVerifyChecklistDto>(`${this.apiUrl}/${jobId}/verify`);
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

	openRegistration(jobId: string, request: OpenRegistrationRequest): Observable<RegistrationFlagsDto> {
		return this.http.post<RegistrationFlagsDto>(`${this.apiUrl}/${jobId}/open-registration`, request);
	}

	// ── Dev-only undo (404 in prod) ──
	getDevUndoStatus(jobId: string): Observable<DevUndoStatusResponse> {
		return this.http.get<DevUndoStatusResponse>(`${this.apiUrl}/${jobId}/dev-undo-status`);
	}

	deleteClonedJob(jobId: string): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/${jobId}/dev-undo`);
	}
}
