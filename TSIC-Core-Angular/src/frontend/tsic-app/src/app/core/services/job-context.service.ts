import { Injectable, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

// Segments that are structural and not job identifiers.
const NON_JOB_SEGMENTS = new Set([
    'tsic',
    'register-player',
    'register-team',
    'family-account',
    'login',
    'admin'
]);

function isValidJobSegment(seg: string): boolean {
    return /^[a-z0-9-]{3,40}$/i.test(seg);
}

function extractJobPath(pathname: string): string | null {
    const segs = pathname.split('/').filter(Boolean);
    for (const s of segs) {
        const lower = s.toLowerCase();
        if (NON_JOB_SEGMENTS.has(lower)) continue;
        if (isValidJobSegment(s)) return s;
        return null; // first non-ignored but invalid -> treat as absent
    }
    return null;
}

@Injectable({ providedIn: 'root' })
export class JobContextService {
    private readonly _jobPath = signal<string | null>(null);

    /** Initialize from current window location; call once during app bootstrap or first route hit. */
    init(): void {
        try {
            const loc = globalThis.location;
            const path = loc?.pathname || '';
            let jp = extractJobPath(path);
            // Fallback: allow query param to be the canonical URL source when no path segment exists
            if (!jp) {
                try {
                    const search = loc?.search || '';
                    const qs = new URLSearchParams(search);
                    const fromQs = qs.get('jobPath') || qs.get('job') || '';
                    if (fromQs && isValidJobSegment(fromQs)) {
                        jp = fromQs;
                    }
                } catch { /* ignore malformed query */ }
            }
            this._jobPath.set(jp);
        } catch {
            this._jobPath.set(null);
        }
    }

    /**
     * Resolve jobPath from Angular route params.
     * This is the CORRECT way to get jobPath - reads from ActivatedRoute paramMap.
     * Falls back to URL parsing (init) if route params are not available.
     */
    resolveFromRoute(route: ActivatedRoute): string {
        // Try to get from route params (/:jobPath pattern)
        const fromParams = route.snapshot.paramMap.get('jobPath')
            || route.parent?.snapshot.paramMap.get('jobPath')
            || route.root.firstChild?.snapshot.paramMap.get('jobPath')
            || '';

        if (fromParams) {
            console.debug('[JobContext] jobPath from route params:', fromParams);
            this._jobPath.set(fromParams);
            return fromParams;
        }

        // Fallback to existing value (from init())
        const existing = this._jobPath();
        if (existing) {
            console.debug('[JobContext] jobPath from existing:', existing);
            return existing;
        }

        console.warn('[JobContext] jobPath not found in route params or URL');
        return '';
    }

    /** Explicit override (e.g., single-job deployments) */
    set(jobPath: string | null): void { this._jobPath.set(jobPath); }

    /** Get current jobPath value */
    jobPath(): string | null { return this._jobPath(); }
}

export { extractJobPath }; // Exported for isolated unit tests if needed.