import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { UserProfileDto, UserProfileUpdateRequest, UsernameAvailabilityResponse } from '@core/api';

/**
 * Role-neutral self-service account API. "me" resolves from the JWT on the
 * server — any signed-in user can read/update their own profile. Used by the
 * adult-registration wizard's account-summary/edit surface (mirrors the
 * club-rep profile edit, which has its own dedicated endpoint).
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
    private readonly http = inject(HttpClient);
    private readonly accountApiUrl = `${environment.apiUrl}/account`;

    /** Read the authenticated user's profile fields. */
    getMyProfile(): Observable<UserProfileDto> {
        return this.http.get<UserProfileDto>(`${this.accountApiUrl}/me`);
    }

    /** Update the authenticated user's profile fields (identity fields locked client-side). */
    updateMyProfile(request: UserProfileUpdateRequest): Observable<void> {
        return this.http.put<void>(`${this.accountApiUrl}/me`, request);
    }

    /**
     * Anonymous pre-check: is this username still free? Advisory only — the server's
     * account-creation gate (UserManager.CreateAsync) stays authoritative. Used by the
     * registration wizards to warn a user before they fill out the whole form.
     */
    checkUsernameAvailable(username: string): Observable<UsernameAvailabilityResponse> {
        const params = new HttpParams().set('username', username);
        return this.http.get<UsernameAvailabilityResponse>(`${this.accountApiUrl}/username-available`, { params });
    }
}
