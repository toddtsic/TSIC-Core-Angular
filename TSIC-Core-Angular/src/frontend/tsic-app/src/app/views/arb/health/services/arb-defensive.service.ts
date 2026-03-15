import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    ArbFlaggedRegistrantDto,
    ArbEmailResultDto,
    ArbSendEmailsRequest,
    ArbSubstitutionVariableDto,
    ArbSubscriptionInfoDto,
    ArbUpdateCcRequest,
    ArbUpdateCcResultDto
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

    sendEmails(request: ArbSendEmailsRequest): Observable<ArbEmailResultDto> {
        return this.http.post<ArbEmailResultDto>(`${this.apiUrl}/send-emails`, request);
    }

    getSubscriptionInfo(registrationId: string): Observable<ArbSubscriptionInfoDto> {
        return this.http.get<ArbSubscriptionInfoDto>(`${this.apiUrl}/subscription-info/${registrationId}`);
    }

    updateCreditCard(request: ArbUpdateCcRequest): Observable<ArbUpdateCcResultDto> {
        return this.http.post<ArbUpdateCcResultDto>(`${this.apiUrl}/update-cc`, request);
    }
}
