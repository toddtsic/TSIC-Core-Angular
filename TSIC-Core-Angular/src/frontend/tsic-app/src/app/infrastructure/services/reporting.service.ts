import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ReportCatalogueEntryDto, ReportCatalogueWriteDto, VerifyStoredProcedureDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class ReportingService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /**
     * Fetches the Type 2 (stored-proc-driven) report catalogue for the current job.
     * Server has already applied visibility filtering — every row is runnable by the caller.
     */
    getCatalogue(): Observable<ReportCatalogueEntryDto[]> {
        return this.http.get<ReportCatalogueEntryDto[]>(`${this.apiUrl}/reporting/catalogue`);
    }

    // -------- SuperUser catalogue editor (Superuser-only endpoints) --------

    getFullCatalogue(): Observable<ReportCatalogueEntryDto[]> {
        return this.http.get<ReportCatalogueEntryDto[]>(`${this.apiUrl}/reporting/catalogue/all`);
    }

    createCatalogueEntry(dto: ReportCatalogueWriteDto): Observable<ReportCatalogueEntryDto> {
        return this.http.post<ReportCatalogueEntryDto>(`${this.apiUrl}/reporting/catalogue`, dto);
    }

    updateCatalogueEntry(reportId: string, dto: ReportCatalogueWriteDto): Observable<ReportCatalogueEntryDto> {
        return this.http.put<ReportCatalogueEntryDto>(`${this.apiUrl}/reporting/catalogue/${reportId}`, dto);
    }

    deleteCatalogueEntry(reportId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/reporting/catalogue/${reportId}`);
    }

    verifyStoredProcedure(name: string): Observable<VerifyStoredProcedureDto> {
        const encoded = encodeURIComponent(name);
        return this.http.get<VerifyStoredProcedureDto>(`${this.apiUrl}/reporting/catalogue/verify-sp?name=${encoded}`);
    }

    downloadReport(
        action: string,
        params?: Record<string, string>
    ): Observable<HttpResponse<Blob>> {
        let url = `${this.apiUrl}/reporting/${action}`;

        if (params && Object.keys(params).length > 0) {
            const searchParams = new URLSearchParams(params);
            url += `?${searchParams.toString()}`;
        }

        return this.http.get(url, {
            responseType: 'blob',
            observe: 'response'
        });
    }

    /**
     * Opens a blob response in a new tab. Browser then routes by content-type:
     * viewable types (PDF, text, iCal) render inline; binary types (Excel) trigger
     * the save dialog. Filename is derived from Content-Disposition for the save
     * path; we don't set anchor.download because that forces "save" and skips the
     * inline view that callers expect for PDFs.
     */
    triggerDownload(response: HttpResponse<Blob>, fallbackFilename = 'TSIC-Export'): void {
        const blob = response.body;
        if (!blob) return;

        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.target = '_blank';
        anchor.rel = 'noopener';
        anchor.click();
        // Defer revoke so Firefox has time to fetch the blob URL for the new tab.
        setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
    }

}
