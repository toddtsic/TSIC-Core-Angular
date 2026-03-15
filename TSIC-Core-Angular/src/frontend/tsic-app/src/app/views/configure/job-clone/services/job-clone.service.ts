import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	JobCloneRequest,
	JobCloneResponse,
	JobCloneSourceDto,
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobCloneService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/job-clone`;

	getSources(): Observable<JobCloneSourceDto[]> {
		return this.http.get<JobCloneSourceDto[]>(`${this.apiUrl}/sources`);
	}

	cloneJob(request: JobCloneRequest): Observable<JobCloneResponse> {
		return this.http.post<JobCloneResponse>(this.apiUrl, request);
	}
}
