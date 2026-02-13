import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    TimeslotConfigurationResponse,
    TimeslotDateDto,
    TimeslotFieldDto,
    CapacityPreviewDto,
    AddTimeslotDateRequest,
    EditTimeslotDateRequest,
    AddTimeslotFieldRequest,
    EditTimeslotFieldRequest,
    CloneDatesRequest,
    CloneFieldsRequest,
    CloneByFieldRequest,
    CloneByDivisionRequest,
    CloneByDowRequest,
    CloneDateRecordRequest,
    CloneFieldDowRequest
} from '@core/api';

// Re-export for consumers
export type {
    TimeslotConfigurationResponse,
    TimeslotDateDto,
    TimeslotFieldDto,
    CapacityPreviewDto,
    AddTimeslotDateRequest,
    EditTimeslotDateRequest,
    AddTimeslotFieldRequest,
    EditTimeslotFieldRequest,
    CloneDatesRequest,
    CloneFieldsRequest,
    CloneByFieldRequest,
    CloneByDivisionRequest,
    CloneByDowRequest,
    CloneDateRecordRequest,
    CloneFieldDowRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class TimeslotService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/timeslot`;

    // ── Configuration ──

    getConfiguration(agegroupId: string): Observable<TimeslotConfigurationResponse> {
        return this.http.get<TimeslotConfigurationResponse>(`${this.apiUrl}/${agegroupId}`);
    }

    getCapacityPreview(agegroupId: string): Observable<CapacityPreviewDto[]> {
        return this.http.get<CapacityPreviewDto[]>(`${this.apiUrl}/${agegroupId}/capacity`);
    }

    // ── Dates CRUD ──

    addDate(request: AddTimeslotDateRequest): Observable<TimeslotDateDto> {
        return this.http.post<TimeslotDateDto>(`${this.apiUrl}/date`, request);
    }

    editDate(request: EditTimeslotDateRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/date`, request);
    }

    deleteDate(ai: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/date/${ai}`);
    }

    cloneDateRecord(request: CloneDateRecordRequest): Observable<TimeslotDateDto> {
        return this.http.post<TimeslotDateDto>(`${this.apiUrl}/date/clone`, request);
    }

    deleteAllDates(agegroupId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/dates/${agegroupId}`);
    }

    // ── Field timeslots CRUD ──

    addFieldTimeslot(request: AddTimeslotFieldRequest): Observable<TimeslotFieldDto[]> {
        return this.http.post<TimeslotFieldDto[]>(`${this.apiUrl}/field`, request);
    }

    editFieldTimeslot(request: EditTimeslotFieldRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/field`, request);
    }

    deleteFieldTimeslot(ai: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/field/${ai}`);
    }

    deleteAllFieldTimeslots(agegroupId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/fields/${agegroupId}`);
    }

    // ── Cloning ──

    cloneDates(request: CloneDatesRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/clone-dates`, request);
    }

    cloneFields(request: CloneFieldsRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/clone-fields`, request);
    }

    cloneByField(request: CloneByFieldRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/clone-by-field`, request);
    }

    cloneByDivision(request: CloneByDivisionRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/clone-by-division`, request);
    }

    cloneByDow(request: CloneByDowRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/clone-by-dow`, request);
    }

    cloneFieldDow(request: CloneFieldDowRequest): Observable<TimeslotFieldDto> {
        return this.http.post<TimeslotFieldDto>(`${this.apiUrl}/clone-field-dow`, request);
    }
}
