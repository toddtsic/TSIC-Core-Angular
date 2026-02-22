import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { EmailLogSummaryDto, EmailLogDetailDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class EmailLogService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/email-log`;

    getEmailLogs(): Observable<EmailLogSummaryDto[]> {
        return this.http.get<EmailLogSummaryDto[]>(this.apiUrl);
    }

    getEmailDetail(emailId: number): Observable<EmailLogDetailDto> {
        return this.http.get<EmailLogDetailDto>(`${this.apiUrl}/${emailId}`);
    }
}
