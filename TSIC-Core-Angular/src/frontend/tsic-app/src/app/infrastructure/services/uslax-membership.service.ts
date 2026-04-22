import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	UsLaxReconciliationCandidateDto,
	UsLaxReconciliationRequest,
	UsLaxReconciliationResponse
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class UsLaxMembershipService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = environment.apiUrl;

	getCandidates(): Observable<UsLaxReconciliationCandidateDto[]> {
		return this.http.get<UsLaxReconciliationCandidateDto[]>(`${this.apiUrl}/uslax-membership/candidates`);
	}

	reconcile(request: UsLaxReconciliationRequest): Observable<UsLaxReconciliationResponse> {
		return this.http.post<UsLaxReconciliationResponse>(`${this.apiUrl}/uslax-membership/reconcile`, request);
	}
}
