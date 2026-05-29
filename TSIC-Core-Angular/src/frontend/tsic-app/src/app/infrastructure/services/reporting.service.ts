import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    JobReportEntryDto,
    JobReportEditorRoleDto,
    JobReportEditorRowDto,
    JobReportEditorUpdateDto,
    JobReportEditorCreateDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class ReportingService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /**
     * Fetches the reports library for the current (Job, Role) — rows from
     * `reporting.JobReports` filtered server-side to (jobId, callerRoles, Active=1).
     * Row existence IS the entitlement; client renders verbatim.
     */
    getCatalogue(): Observable<JobReportEntryDto[]> {
        return this.http.get<JobReportEntryDto[]>(`${this.apiUrl}/reporting/catalogue`);
    }

    // ── SuperUser editor (per-Job, per-Role) ──

    getEditorRoles(): Observable<JobReportEditorRoleDto[]> {
        return this.http.get<JobReportEditorRoleDto[]>(`${this.apiUrl}/reporting/editor/job-roles`);
    }

    getEditorRows(roleId: string): Observable<JobReportEditorRowDto[]> {
        const params = new HttpParams().set('roleId', roleId);
        return this.http.get<JobReportEditorRowDto[]>(`${this.apiUrl}/reporting/editor`, { params });
    }

    updateEditorRow(
        jobReportId: string,
        dto: JobReportEditorUpdateDto
    ): Observable<JobReportEditorRowDto> {
        return this.http.put<JobReportEditorRowDto>(
            `${this.apiUrl}/reporting/editor/${jobReportId}`,
            dto
        );
    }

    createEditorRow(dto: JobReportEditorCreateDto): Observable<JobReportEditorRowDto> {
        return this.http.post<JobReportEditorRowDto>(`${this.apiUrl}/reporting/editor`, dto);
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
     * Hands the blob to the browser as a download — every type, including PDF.
     * Earlier this method opened PDFs inline via target="_blank", but rapid
     * repeat clicks tripped Chrome's popup blocker (first click had a user
     * gesture, subsequent ones did not), silently swallowing the new tab while
     * the toast still claimed success. anchor.download dodges popup blocking
     * entirely; Chrome / Edge auto-open the downloaded PDF in their built-in
     * viewer, so the inline-viewing UX survives. Filename comes from the
     * server's Content-Disposition (blob: URLs strip it otherwise).
     */
    triggerDownload(response: HttpResponse<Blob>, fallbackFilename = 'TSIC-Export'): void {
        const blob = response.body;
        if (!blob) return;

        const contentType = (response.headers.get('Content-Type') ?? '').toLowerCase();
        const filename = this.parseContentDispositionFilename(response)
            ?? this.fallbackWithExtension(contentType, fallbackFilename);

        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.rel = 'noopener';
        anchor.download = filename;

        anchor.click();
        // Defer revoke so the browser has time to fetch the blob URL.
        setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
    }

    private parseContentDispositionFilename(response: HttpResponse<Blob>): string | null {
        const cd = response.headers.get('Content-Disposition') ?? '';
        // RFC 5987 (UTF-8) form first: filename*=UTF-8''<percent-encoded>
        const utf8 = cd.match(/filename\*\s*=\s*UTF-8''([^;]+)/i);
        if (utf8) {
            try { return decodeURIComponent(utf8[1].trim()); } catch { /* fall through */ }
        }
        const plain = cd.match(/filename\s*=\s*"?([^";]+)"?/i);
        return plain ? plain[1].trim() : null;
    }

    private fallbackWithExtension(contentType: string, base: string): string {
        if (contentType.includes('spreadsheetml')) return `${base}.xlsx`;
        if (contentType.includes('ms-excel')) return `${base}.xls`;
        if (contentType.includes('pdf')) return `${base}.pdf`;
        if (contentType.includes('calendar')) return `${base}.ics`;
        if (contentType.includes('csv')) return `${base}.csv`;
        if (contentType.includes('rtf')) return `${base}.rtf`;
        if (contentType.includes('json')) return `${base}.json`;
        if (contentType.startsWith('text/')) return `${base}.txt`;
        return base;
    }
}
