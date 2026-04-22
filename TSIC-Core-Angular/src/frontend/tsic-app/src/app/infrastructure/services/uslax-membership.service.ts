import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	UsLaxEmailRequest,
	UsLaxEmailResponse,
	UsLaxMembershipRole,
	UsLaxReconciliationCandidateDto,
	UsLaxReconciliationRequest,
	UsLaxReconciliationResponse
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class UsLaxMembershipService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = environment.apiUrl;

	getCandidates(role: UsLaxMembershipRole): Observable<UsLaxReconciliationCandidateDto[]> {
		const params = new HttpParams().set('role', role);
		return this.http.get<UsLaxReconciliationCandidateDto[]>(`${this.apiUrl}/uslax-membership/candidates`, { params });
	}

	reconcile(request: UsLaxReconciliationRequest): Observable<UsLaxReconciliationResponse> {
		return this.http.post<UsLaxReconciliationResponse>(`${this.apiUrl}/uslax-membership/reconcile`, request);
	}

	sendEmail(request: UsLaxEmailRequest): Observable<UsLaxEmailResponse> {
		return this.http.post<UsLaxEmailResponse>(`${this.apiUrl}/uslax-membership/email`, request);
	}
}
