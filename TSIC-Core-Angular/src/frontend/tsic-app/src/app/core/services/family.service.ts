import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface FamilyRegistrationRequest {
    username: string;
    password: string;
    primary: {
        firstName: string;
        lastName: string;
        cellphone: string;
        email: string;
    };
    secondary: {
        firstName: string;
        lastName: string;
        cellphone: string;
        email: string;
    };
    address: {
        streetAddress: string;
        city: string;
        state: string;
        postalCode: string;
    };
    children: Array<{
        firstName: string;
        lastName: string;
        gender: string;
        dob?: string;
        email?: string;
        phone?: string;
    }>;
}

export interface FamilyRegistrationResponse {
    success: boolean;
    familyUserId?: string;
    familyId?: string;
    message?: string;
}

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
}
