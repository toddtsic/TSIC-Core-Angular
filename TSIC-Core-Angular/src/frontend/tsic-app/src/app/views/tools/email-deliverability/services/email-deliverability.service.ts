import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	SuppressionEntryDto,
	SuppressionRemoveResultDto,
	EmailInvestigateResultDto,
	PlayerSentEmailDto
} from '@core/api';

// Re-export for component consumers
export type { SuppressionEntryDto, SuppressionRemoveResultDto, EmailInvestigateResultDto, PlayerSentEmailDto };

/**
 * Player-facing email deliverability API. The server resolves the caller's own family emails
 * (mom/dad/each player, across all jobs) from the JWT — the client never sends an address to
 * check. Unsuppress and test-send are refused server-side for any address outside that set.
 */
@Injectable({ providedIn: 'root' })
export class EmailDeliverabilityService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/my-email-deliverability`;

	/** Suppression status of every sendable email on the caller's family. */
	getStatus(): Observable<SuppressionEntryDto[]> {
		return this.http.get<SuppressionEntryDto[]>(`${this.apiUrl}/status`);
	}

	/** Remove one of the caller's own addresses from the SES suppression list. */
	unsuppress(email: string): Observable<SuppressionRemoveResultDto> {
		return this.http.post<SuppressionRemoveResultDto>(`${this.apiUrl}/unsuppress`, { email });
	}

	/** Send a real test message to one of the caller's own addresses; reports which side a failure is on. */
	testSend(email: string): Observable<EmailInvestigateResultDto> {
		return this.http.post<EmailInvestigateResultDto>(`${this.apiUrl}/test-send`, { email });
	}

	/** Messages our system dispatched to any of the caller's own addresses, across all jobs (newest first). */
	getSentHistory(): Observable<PlayerSentEmailDto[]> {
		return this.http.get<PlayerSentEmailDto[]>(`${this.apiUrl}/sent-history`);
	}
}
