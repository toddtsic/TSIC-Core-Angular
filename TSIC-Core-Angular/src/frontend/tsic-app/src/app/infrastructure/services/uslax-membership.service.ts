import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { switchMap, takeWhile, last, map } from 'rxjs/operators';
import { environment } from '@environments/environment';
import type {
	UsLaxEmailRequest,
	UsLaxEmailStartResponse,
	UsLaxMembershipRole,
	UsLaxReconciliationCandidateDto,
	UsLaxReconciliationRequest,
	UsLaxReconciliationResponse,
	EmailBatchJobStatus
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

	/** Starts the background USLax email batch; returns the up-front skip rollup + a handle to poll. */
	startEmail(request: UsLaxEmailRequest): Observable<UsLaxEmailStartResponse> {
		return this.http.post<UsLaxEmailStartResponse>(`${this.apiUrl}/uslax-membership/email`, request);
	}

	getEmailStatus(batchJobId: string): Observable<EmailBatchJobStatus> {
		return this.http.get<EmailBatchJobStatus>(`${this.apiUrl}/uslax-membership/email/${batchJobId}/status`);
	}

	/**
	 * Starts the batch then polls every second, emitting the start rollup + FINAL status together
	 * once the engine reports done. Same background+poll contract as the other batch paths.
	 */
	sendEmailAndAwait(
		request: UsLaxEmailRequest
	): Observable<{ start: UsLaxEmailStartResponse; status: EmailBatchJobStatus }> {
		return this.startEmail(request).pipe(
			switchMap(start =>
				timer(0, 1000).pipe(
					switchMap(() => this.getEmailStatus(start.batchJobId)),
					takeWhile(s => !s.done, true),
					last(),
					map(status => ({ start, status }))
				)
			)
		);
	}
}
