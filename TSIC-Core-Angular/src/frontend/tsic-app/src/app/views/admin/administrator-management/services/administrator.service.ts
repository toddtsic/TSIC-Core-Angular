import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AdministratorDto,
    AddAdministratorRequest,
    UpdateAdministratorRequest,
    UserSearchResultDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class AdministratorService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/administrators`;

    getAdministrators(): Observable<AdministratorDto[]> {
        return this.http.get<AdministratorDto[]>(this.apiUrl);
    }

    addAdministrator(request: AddAdministratorRequest): Observable<AdministratorDto> {
        return this.http.post<AdministratorDto>(this.apiUrl, request);
    }

    updateAdministrator(registrationId: string, request: UpdateAdministratorRequest): Observable<AdministratorDto> {
        return this.http.put<AdministratorDto>(`${this.apiUrl}/${registrationId}`, request);
    }

    deleteAdministrator(registrationId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${registrationId}`);
    }

    toggleStatus(registrationId: string): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/${registrationId}/toggle-status`, {});
    }

    searchUsers(query: string): Observable<UserSearchResultDto[]> {
        return this.http.get<UserSearchResultDto[]>(`${this.apiUrl}/users/search`, {
            params: { q: query }
        });
    }

    setPrimaryContact(registrationId: string): Observable<AdministratorDto[]> {
        return this.http.put<AdministratorDto[]>(`${this.apiUrl}/${registrationId}/primary-contact`, {});
    }
}
