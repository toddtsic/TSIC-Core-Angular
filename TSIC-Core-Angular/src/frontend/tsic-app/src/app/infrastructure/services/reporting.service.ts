import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';

@Injectable({ providedIn: 'root' })
export class ReportingService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    /**
     * Downloads a report from the API as a blob.
     * The action maps to the backend ReportingController endpoint name.
     */
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
     * Triggers a browser file download from a blob response.
     */
    triggerDownload(response: HttpResponse<Blob>, fallbackFilename = 'TSIC-Export'): void {
        const blob = response.body;
        if (!blob) return;

        // Extract filename from Content-Disposition header if available
        const contentDisposition = response.headers.get('Content-Disposition');
        let filename = fallbackFilename;

        if (contentDisposition) {
            const match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
            if (match && match[1]) {
                filename = match[1].replace(/['"]/g, '');
            }
        }

        // If no extension in filename, derive from content type
        if (!filename.includes('.')) {
            const contentType = response.headers.get('Content-Type') ?? blob.type;
            const ext = this.getExtensionFromContentType(contentType);
            if (ext) {
                filename += ext;
            }
        }

        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        anchor.click();
        window.URL.revokeObjectURL(url);
    }

    private getExtensionFromContentType(contentType: string | null): string | null {
        if (!contentType) return null;
        const type = contentType.split(';')[0].trim().toLowerCase();
        const map: Record<string, string> = {
            'application/pdf': '.pdf',
            'application/rtf': '.rtf',
            'application/ms-excel': '.xls',
            'application/vnd.ms-excel': '.xls',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': '.xlsx',
            'text/calendar': '.ics',
            'text/plain': '.txt',
        };
        return map[type] ?? null;
    }
}
