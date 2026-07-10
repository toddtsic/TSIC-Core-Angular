import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { switchMap, takeWhile, last } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import type { MyRosterResponseDto } from '@core/api/models/MyRosterResponseDto';
import type { MyRosterBatchEmailRequest } from '@core/api/models/MyRosterBatchEmailRequest';
import type { EmailBatchHandle } from '@core/api/models/EmailBatchHandle';
import type { EmailBatchJobStatus } from '@core/api/models/EmailBatchJobStatus';

@Injectable({ providedIn: 'root' })
export class MyRosterService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/my-roster`;

    get(): Observable<MyRosterResponseDto> {
        return this.http.get<MyRosterResponseDto>(this.apiUrl);
    }

    /** Downloads the caller's own team roster as a PDF listing (same server-side gate as get()). */
    downloadPdf(): Observable<HttpResponse<Blob>> {
        return this.http.get(`${this.apiUrl}/pdf`, { responseType: 'blob', observe: 'response' });
    }

    /** Starts the background roster-email batch; returns a handle to poll. */
    startEmail(request: MyRosterBatchEmailRequest): Observable<EmailBatchHandle> {
        return this.http.post<EmailBatchHandle>(`${this.apiUrl}/email`, request);
    }

    getEmailStatus(batchJobId: string): Observable<EmailBatchJobStatus> {
        return this.http.get<EmailBatchJobStatus>(`${this.apiUrl}/email/${batchJobId}/status`);
    }

    /**
     * Starts the batch then polls every second, emitting the FINAL status once the engine reports
     * done. Same background+poll contract as every other batch email path.
     */
    sendEmailAndAwait(request: MyRosterBatchEmailRequest): Observable<EmailBatchJobStatus> {
        return this.startEmail(request).pipe(
            switchMap(handle =>
                timer(0, 1000).pipe(
                    switchMap(() => this.getEmailStatus(handle.jobId)),
                    takeWhile(s => !s.done, true),
                    last()
                )
            )
        );
    }
}
