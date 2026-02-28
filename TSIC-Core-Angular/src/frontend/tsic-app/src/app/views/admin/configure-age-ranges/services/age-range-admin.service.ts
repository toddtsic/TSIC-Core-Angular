import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  AgeRangeDto,
  CreateAgeRangeRequest,
  UpdateAgeRangeRequest
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class AgeRangeAdminService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/AgeRanges`;

  getAllAgeRanges(): Observable<AgeRangeDto[]> {
    return this.http.get<AgeRangeDto[]>(`${this.apiUrl}/admin`);
  }

  createAgeRange(request: CreateAgeRangeRequest): Observable<AgeRangeDto> {
    return this.http.post<AgeRangeDto>(this.apiUrl, request);
  }

  updateAgeRange(ageRangeId: number, request: UpdateAgeRangeRequest): Observable<AgeRangeDto> {
    return this.http.put<AgeRangeDto>(`${this.apiUrl}/${ageRangeId}`, request);
  }

  deleteAgeRange(ageRangeId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${ageRangeId}`);
  }
}
