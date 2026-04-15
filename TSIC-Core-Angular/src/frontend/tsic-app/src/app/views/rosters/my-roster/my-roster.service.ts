import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import type { MyRosterResponseDto } from '@core/api/models/MyRosterResponseDto';
import type { MyRosterBatchEmailRequest } from '@core/api/models/MyRosterBatchEmailRequest';
import type { BatchEmailResponse } from '@core/api/models/BatchEmailResponse';

@Injectable({ providedIn: 'root' })
export class MyRosterService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/my-roster`;

    get(): Observable<MyRosterResponseDto> {
        return this.http.get<MyRosterResponseDto>(this.apiUrl);
    }

    sendEmail(request: MyRosterBatchEmailRequest): Observable<BatchEmailResponse> {
        return this.http.post<BatchEmailResponse>(`${this.apiUrl}/email`, request);
    }
}
