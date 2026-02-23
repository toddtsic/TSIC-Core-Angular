import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { MobileScorerDto, CreateMobileScorerRequest, UpdateMobileScorerRequest } from '@core/api';

@Injectable({ providedIn: 'root' })
export class MobileScorersService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/mobile-scorers`;

    getScorers(): Observable<MobileScorerDto[]> {
        return this.http.get<MobileScorerDto[]>(this.apiUrl);
    }

    createScorer(request: CreateMobileScorerRequest): Observable<MobileScorerDto> {
        return this.http.post<MobileScorerDto>(this.apiUrl, request);
    }

    updateScorer(registrationId: string, request: UpdateMobileScorerRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/${registrationId}`, request);
    }

    deleteScorer(registrationId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${registrationId}`);
    }
}
