import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { skipErrorToast } from '@infrastructure/interceptors/http-error-context';
import type {
    AdministratorDto,
    AddAdministratorRequest,
    UpdateAdministratorRequest,
    UserSearchResponseDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class AdministratorService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/administrators`;

    getAdministrators(): Observable<AdministratorDto[]> {
        return this.http.get<AdministratorDto[]>(this.apiUrl);
    }

    // skipErrorToast: the form modal shows save failures inline (saveFailed) — without the
    // skip, the global interceptor toasts the same error a second time over the open dialog.
    addAdministrator(request: AddAdministratorRequest): Observable<AdministratorDto> {
        return this.http.post<AdministratorDto>(this.apiUrl, request, { context: skipErrorToast() });
    }

    updateAdministrator(registrationId: string, request: UpdateAdministratorRequest): Observable<AdministratorDto> {
        return this.http.put<AdministratorDto>(`${this.apiUrl}/${registrationId}`, request, { context: skipErrorToast() });
    }

    deleteAdministrator(registrationId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${registrationId}`);
    }

    toggleStatus(registrationId: string): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/${registrationId}/toggle-status`, {});
    }

    activateAll(): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/activate-all`, {});
    }

    deactivateAll(): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/deactivate-all`, {});
    }

    /** Lane model (AM-004): candidates depend on the role being granted — role is required.
     *  Empty results carry an emptyReason so the modal can show the right funnel message. */
    searchUsers(query: string, role: string): Observable<UserSearchResponseDto> {
        return this.http.get<UserSearchResponseDto>(`${this.apiUrl}/users/search`, {
            params: { q: query, role }
        });
    }

    setPrimaryContact(registrationId: string): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/${registrationId}/primary-contact`, {});
    }
}
