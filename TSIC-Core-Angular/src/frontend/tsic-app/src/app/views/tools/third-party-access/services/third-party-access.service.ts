import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { GrantThirdPartyAccessRequest, ThirdPartyAccessOverviewDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class ThirdPartyAccessService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/third-party-access`;

	getOverview(): Observable<ThirdPartyAccessOverviewDto> {
		return this.http.get<ThirdPartyAccessOverviewDto>(`${this.apiUrl}/overview`);
	}

	grant(jobId: string, request: GrantThirdPartyAccessRequest): Observable<ThirdPartyAccessOverviewDto> {
		return this.http.post<ThirdPartyAccessOverviewDto>(`${this.apiUrl}/jobs/${jobId}/grant`, request);
	}

	disable(jobId: string): Observable<ThirdPartyAccessOverviewDto> {
		return this.http.post<ThirdPartyAccessOverviewDto>(`${this.apiUrl}/jobs/${jobId}/disable`, {});
	}
}
