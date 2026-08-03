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

	// The release-flow callers (verify checklist, releasable admins, release-site,
	// release-admins, open-registration) went with the release page. Each duplicated a
	// Configure → Job screen, and release-site wrote Jobs.bSuspendPublic under a name
	// that claimed far more than that column does — it gates TSIC-Events listing and
	// cross-sell, not the job's own public pages. The API endpoints still exist and are
	// now unreferenced; removing them is a separate pass.

	// ── Sandbox-only undo (404s outside Development/Staging) ──
	// Consumed by the Configure → Job delete panel.
	getDevUndoStatus(jobId: string): Observable<DevUndoStatusResponse> {
		return this.http.get<DevUndoStatusResponse>(`${this.apiUrl}/${jobId}/dev-undo-status`);
	}

	deleteClonedJob(jobId: string): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/${jobId}/dev-undo`);
	}
}
