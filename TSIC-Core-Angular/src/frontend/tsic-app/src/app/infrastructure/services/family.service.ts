import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import type {
    FamilyRegistrationRequest,
    FamilyRegistrationResponse,
    FamilyUpdateRequest,
    FamilyProfileResponse
} from '../api';

@Injectable({ providedIn: 'root' })
export class FamilyService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl + '/family';

    // Signals for command-style usage
    public readonly createLoading = signal(false);
    public readonly createError = signal<string | null>(null);
    public readonly createResult = signal<FamilyRegistrationResponse | null>(null);

    registerFamily(request: FamilyRegistrationRequest) {
        this.createLoading.set(true);
        this.createError.set(null);
        this.createResult.set(null);
        return this.http.post<FamilyRegistrationResponse>(`${this.apiUrl}/register`, request);
    }

    updateFamily(request: FamilyUpdateRequest) {
        this.createLoading.set(true);
        this.createError.set(null);
        this.createResult.set(null);
        return this.http.put<FamilyRegistrationResponse>(`${this.apiUrl}/update`, request);
    }

    getMyFamily() {
        return this.http.get<FamilyProfileResponse>(`${this.apiUrl}/me`);
    }
}
