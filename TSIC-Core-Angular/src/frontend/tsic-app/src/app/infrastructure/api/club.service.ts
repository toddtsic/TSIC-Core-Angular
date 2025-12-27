import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { ClubRepRegistrationRequest, ClubRepRegistrationResponse, ClubSearchResult } from './models';

@Injectable({ providedIn: 'root' })
export class ClubService {
    private readonly http = inject(HttpClient);

    private get apiBase(): string {
        return environment.apiUrl.endsWith('/api')
            ? environment.apiUrl
            : `${environment.apiUrl}/api`;
    }

    /**
     * Search for existing clubs by name (fuzzy matching)
     */
    async searchClubs(query: string, state?: string): Promise<ClubSearchResult[]> {
        const params: any = { q: query };
        if (state) params.state = state;

        return firstValueFrom(
            this.http.get<ClubSearchResult[]>(`${this.apiBase}/clubs/search`, { params })
        );
    }

    /**
     * Register a new club and club rep
     */
    async registerClub(request: ClubRepRegistrationRequest): Promise<ClubRepRegistrationResponse> {
        return firstValueFrom(
            this.http.post<ClubRepRegistrationResponse>(`${this.apiBase}/club-reps/register`, request)
        );
    }
}
