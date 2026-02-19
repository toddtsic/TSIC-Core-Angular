import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { JobConfigDto, JobConfigLookupsDto, UpdateJobConfigRequest } from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobConfigService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/job-config`;

	getConfig(): Observable<JobConfigDto> {
		return this.http.get<JobConfigDto>(this.apiUrl);
	}

	getLookups(): Observable<JobConfigLookupsDto> {
		return this.http.get<JobConfigLookupsDto>(`${this.apiUrl}/lookups`);
	}

	updateConfig(request: UpdateJobConfigRequest): Observable<JobConfigDto> {
		return this.http.put<JobConfigDto>(this.apiUrl, request);
	}
}
