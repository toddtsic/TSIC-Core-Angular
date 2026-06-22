import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { JobVisibilityDto, UpdateJobVisibilityRequest } from '@core/api';

/**
 * SuperUser "Quick Links" editor HTTP access. Reads/writes the current
 * (logged-in) job's landing-hero visibility flags — the backend resolves the
 * job from the JWT, so no jobId/jobPath is sent. Partial PUTs let the UI save
 * one toggle at a time.
 */
@Injectable({ providedIn: 'root' })
export class QuickLinksService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/job-visibility`;

	get(): Observable<JobVisibilityDto> {
		return this.http.get<JobVisibilityDto>(this.apiUrl);
	}

	save(patch: UpdateJobVisibilityRequest): Observable<void> {
		return this.http.put<void>(this.apiUrl, patch);
	}
}
