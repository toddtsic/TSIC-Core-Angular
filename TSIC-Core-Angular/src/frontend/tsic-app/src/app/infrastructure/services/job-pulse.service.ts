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

    /**
     * Monotonic stamp for pulse requests. A response may only write the signal if no
     * NEWER load()/clear() has happened since it left — plain last-response-wins let a
     * cross-job role switch land the PREVIOUS job's pulse on the new job's page (the
     * band then rendered that job's lifecycle phase, e.g. "Registration is coming soon",
     * until a browser refresh). Two jobs are genuinely in flight during a switch, so the
     * guard is on the WRITE, not on cancellation: whoever answers late is simply ignored.
     */
    private requestSeq = 0;

    load(jobPath: string): void {
        const seq = ++this.requestSeq;
        if (!jobPath) {
            this._pulse.set(null);
            return;
        }
        this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
            .subscribe({
                next: p => { if (seq === this.requestSeq) this._pulse.set(p); },
                error: () => { if (seq === this.requestSeq) this._pulse.set(null); },
            });
    }

    /** Drop the pulse AND invalidate anything in flight — otherwise a request issued
     *  before the clear (logout, job teardown) answers afterwards and resurrects it. */
    clear(): void {
        this.requestSeq++;
        this._pulse.set(null);
    }
}
