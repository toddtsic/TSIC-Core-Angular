import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import type { JobPulseDto } from '@core/api';

/**
 * Session-scoped holder for the current job's pulse (job-level flags + per-user
 * overlay when authenticated). Consumers call `load(jobPath)` on job / user
 * transitions; the signal drives conditional menu items and feature gates.
 */
@Injectable({ providedIn: 'root' })
export class JobPulseService {
    private readonly http = inject(HttpClient);

    private readonly _pulse = signal<JobPulseDto | null>(null);
    readonly pulse = this._pulse.asReadonly();

    load(jobPath: string): void {
        if (!jobPath) {
            this._pulse.set(null);
            return;
        }
        this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
            .subscribe({
                next: p => this._pulse.set(p),
                error: () => this._pulse.set(null),
            });
    }

    clear(): void {
        this._pulse.set(null);
    }
}
