import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    FieldDto,
    FieldManagementResponse,
    CreateFieldRequest,
    UpdateFieldRequest,
    AssignFieldsRequest,
    RemoveFieldsRequest
} from '@core/api';

// Re-export for consumers
export type {
    FieldDto,
    FieldManagementResponse,
    CreateFieldRequest,
    UpdateFieldRequest,
    AssignFieldsRequest,
    RemoveFieldsRequest,
    LeagueSeasonFieldDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class FieldManagementService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/field`;

    getFieldManagementData(): Observable<FieldManagementResponse> {
        return this.http.get<FieldManagementResponse>(this.apiUrl);
    }

    createField(request: CreateFieldRequest): Observable<FieldDto> {
        return this.http.post<FieldDto>(this.apiUrl, request);
    }

    updateField(request: UpdateFieldRequest): Observable<void> {
        return this.http.put<void>(this.apiUrl, request);
    }

    deleteField(fieldId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${fieldId}`);
    }

    assignFields(request: AssignFieldsRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/assign`, request);
    }

    removeFields(request: RemoveFieldsRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/remove`, request);
    }
}
