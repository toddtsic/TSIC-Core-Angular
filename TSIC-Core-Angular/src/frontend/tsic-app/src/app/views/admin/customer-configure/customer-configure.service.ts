import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import type {
  CustomerListDto,
  CustomerDetailDto,
  TimezoneDto,
  CreateCustomerRequest,
  UpdateCustomerRequest
} from '../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class CustomerConfigureService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/customer-configure`;

  getAll(): Observable<CustomerListDto[]> {
    return this.http.get<CustomerListDto[]>(this.apiUrl);
  }

  getById(customerId: string): Observable<CustomerDetailDto> {
    return this.http.get<CustomerDetailDto>(`${this.apiUrl}/${customerId}`);
  }

  getTimezones(): Observable<TimezoneDto[]> {
    return this.http.get<TimezoneDto[]>(`${this.apiUrl}/timezones`);
  }

  create(request: CreateCustomerRequest): Observable<CustomerDetailDto> {
    return this.http.post<CustomerDetailDto>(this.apiUrl, request);
  }

  update(customerId: string, request: UpdateCustomerRequest): Observable<CustomerDetailDto> {
    return this.http.put<CustomerDetailDto>(`${this.apiUrl}/${customerId}`, request);
  }

  delete(customerId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${customerId}`);
  }
}
