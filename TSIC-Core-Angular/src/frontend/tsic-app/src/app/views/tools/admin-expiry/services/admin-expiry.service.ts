import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AdminExpiryCustomerDto, UpdateAdminExpiryRequest } from '@core/api';

@Injectable({ providedIn: 'root' })
export class AdminExpiryService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/admin-expiry`;

	getExpiredJobs(): Observable<AdminExpiryCustomerDto[]> {
		return this.http.get<AdminExpiryCustomerDto[]>(`${this.apiUrl}/expired-jobs`);
	}

	updateExpiry(jobId: string, request: UpdateAdminExpiryRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/jobs/${jobId}`, request);
	}
}
