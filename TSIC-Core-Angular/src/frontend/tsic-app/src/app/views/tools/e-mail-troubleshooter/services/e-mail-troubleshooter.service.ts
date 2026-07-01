import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	SuppressionEntryDto,
	SuppressionRemoveResultDto,
	EmailInvestigateResultDto
} from '@core/api';

// Re-export for component consumers
export type { SuppressionEntryDto, SuppressionRemoveResultDto, EmailInvestigateResultDto };

/**
 * Admin E-Mail Troubleshooter API. Backed by SES (suppression list v2 + forced test send).
 * The backend processes each address independently; we just hand it the parsed list.
 */
@Injectable({ providedIn: 'root' })
export class EmailTroubleshooterService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/email-troubleshooter`;

	checkSuppression(emails: string[]): Observable<SuppressionEntryDto[]> {
		return this.http.post<SuppressionEntryDto[]>(`${this.apiUrl}/suppression/check`, { emails });
	}

	removeSuppression(emails: string[]): Observable<SuppressionRemoveResultDto[]> {
		return this.http.post<SuppressionRemoveResultDto[]>(`${this.apiUrl}/suppression/remove`, { emails });
	}

	investigate(emails: string[]): Observable<EmailInvestigateResultDto[]> {
		return this.http.post<EmailInvestigateResultDto[]>(`${this.apiUrl}/investigate`, { emails });
	}
}
