import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, debounceTime, distinctUntilChanged } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  DiscountCodeDto,
  AddDiscountCodeRequest,
  BulkAddDiscountCodeRequest,
  UpdateDiscountCodeRequest,
  BatchUpdateStatusRequest
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class DiscountCodeService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/DiscountCodes`;

  /**
   * Get all discount codes for the authenticated user's job.
   */
  getDiscountCodes(): Observable<DiscountCodeDto[]> {
    return this.http.get<DiscountCodeDto[]>(this.apiUrl);
  }

  /**
   * Add a new discount code.
   */
  addDiscountCode(request: AddDiscountCodeRequest): Observable<DiscountCodeDto> {
    return this.http.post<DiscountCodeDto>(this.apiUrl, request);
  }

  /**
   * Bulk generate discount codes with sequential pattern.
   */
  bulkAddDiscountCodes(request: BulkAddDiscountCodeRequest): Observable<DiscountCodeDto[]> {
    return this.http.post<DiscountCodeDto[]>(`${this.apiUrl}/bulk`, request);
  }

  /**
   * Update an existing discount code.
   */
  updateDiscountCode(ai: number, request: UpdateDiscountCodeRequest): Observable<DiscountCodeDto> {
    return this.http.put<DiscountCodeDto>(`${this.apiUrl}/${ai}`, request);
  }

  /**
   * Delete a discount code (only if not used).
   */
  deleteDiscountCode(ai: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${ai}`);
  }

  /**
   * Batch activate/deactivate discount codes.
   */
  batchUpdateStatus(request: BatchUpdateStatusRequest): Observable<{ updatedCount: number }> {
    return this.http.post<{ updatedCount: number }>(`${this.apiUrl}/batch-status`, request);
  }

  /**
   * Check if a discount code already exists for the job (debounced for real-time validation).
   */
  checkCodeExists(codeName: string): Observable<{ exists: boolean }> {
    return this.http.get<{ exists: boolean }>(`${this.apiUrl}/check-exists/${encodeURIComponent(codeName)}`).pipe(
      debounceTime(300),
      distinctUntilChanged()
    );
  }
}
