import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { switchMap, takeWhile, last } from 'rxjs/operators';
import { environment } from '@environments/environment';
import type {
    ArbFlaggedRegistrantDto,
    ArbSendEmailsRequest,
    ArbSubstitutionVariableDto,
    ArbSubscriptionInfoDto,
    ArbUpdateCcRequest,
    ArbUpdateCcResultDto,
    EmailBatchHandle,
    EmailBatchJobStatus
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class ArbDefensiveService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/arb-defensive`;

    getFlagged(type: number): Observable<ArbFlaggedRegistrantDto[]> {
        const params = new HttpParams().set('type', type.toString());
        return this.http.get<ArbFlaggedRegistrantDto[]>(`${this.apiUrl}/flagged`, { params });
    }

    getSubstitutionVariables(): Observable<ArbSubstitutionVariableDto[]> {
        return this.http.get<ArbSubstitutionVariableDto[]>(`${this.apiUrl}/substitution-variables`);
    }

    /** Starts the background ARB defensive batch; returns a job handle to poll. */
    startSendEmails(request: ArbSendEmailsRequest): Observable<EmailBatchHandle> {
        return this.http.post<EmailBatchHandle>(`${this.apiUrl}/send-emails`, request);
    }

    getSendStatus(batchJobId: string): Observable<EmailBatchJobStatus> {
        return this.http.get<EmailBatchJobStatus>(`${this.apiUrl}/send-emails/${batchJobId}/status`);
    }

    /**
     * Starts the batch then polls its status every second, emitting the FINAL status once the
     * engine reports done. Same background+poll contract as the Search Registrations batch path.
     */
    sendEmailsAndAwait(request: ArbSendEmailsRequest): Observable<EmailBatchJobStatus> {
        return this.startSendEmails(request).pipe(
            switchMap(handle =>
                timer(0, 1000).pipe(
                    switchMap(() => this.getSendStatus(handle.jobId)),
                    takeWhile(status => !status.done, true),
                    last()
                )
            )
        );
    }

    getSubscriptionInfo(registrationId: string): Observable<ArbSubscriptionInfoDto> {
        return this.http.get<ArbSubscriptionInfoDto>(`${this.apiUrl}/subscription-info/${registrationId}`);
    }

    updateCreditCard(request: ArbUpdateCcRequest): Observable<ArbUpdateCcResultDto> {
        return this.http.post<ArbUpdateCcResultDto>(`${this.apiUrl}/update-cc`, request);
    }
}
