import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  BulletinAdminDto,
  CreateBulletinRequest,
  UpdateBulletinRequest,
  BatchUpdateBulletinStatusRequest,
  BulletinTokenCatalogEntryDto,
  BulletinPreviewRequest,
  BulletinPreviewResponse,
  AiFormatResponse,
  AiFormatBulletinRequest
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class BulletinAdminService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Bulletins`;

  getAllBulletins(): Observable<BulletinAdminDto[]> {
    return this.http.get<BulletinAdminDto[]>(`${this.apiUrl}/admin`);
  }

  createBulletin(request: CreateBulletinRequest): Observable<BulletinAdminDto> {
    return this.http.post<BulletinAdminDto>(this.apiUrl, request);
  }

  updateBulletin(bulletinId: string, request: UpdateBulletinRequest): Observable<BulletinAdminDto> {
    return this.http.put<BulletinAdminDto>(`${this.apiUrl}/${bulletinId}`, request);
  }

  deleteBulletin(bulletinId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${bulletinId}`);
  }

  batchUpdateStatus(request: BatchUpdateBulletinStatusRequest): Observable<{ updatedCount: number }> {
    return this.http.post<{ updatedCount: number }>(`${this.apiUrl}/batch-status`, request);
  }

  aiComposeBulletin(prompt: string): Observable<{ subject: string; body: string }> {
    return this.http.post<{ subject: string; body: string }>(`${environment.apiUrl}/ai-compose/bulletin`, { prompt });
  }

  // SuperUser-only: author-facing token catalog with metadata and sample HTML.
  getTokenCatalog(): Observable<BulletinTokenCatalogEntryDto[]> {
    return this.http.get<BulletinTokenCatalogEntryDto[]>(`${this.apiUrl}/token-catalog`);
  }

  // SuperUser-only: resolve !TOKEN markers in arbitrary HTML, with optional pulse override.
  previewBulletin(request: BulletinPreviewRequest): Observable<BulletinPreviewResponse> {
    return this.http.post<BulletinPreviewResponse>(`${this.apiUrl}/preview`, request);
  }

  // Admin+: reformat existing bulletin HTML for better UX; inserts token markers where appropriate.
  aiFormatBulletin(html: string): Observable<AiFormatResponse> {
    const body: AiFormatBulletinRequest = { html };
    return this.http.post<AiFormatResponse>(`${environment.apiUrl}/ai-compose/bulletin/format`, body);
  }
}
