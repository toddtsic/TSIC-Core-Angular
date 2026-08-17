import { Injectable, inject } from '@angular/core';
import { Router, NavigationEnd, type ActivatedRouteSnapshot } from '@angular/router';
import { filter } from 'rxjs/operators';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { resolveJobPath } from '@infrastructure/navigation/job-path';
import { JobService } from '@infrastructure/services/job.service';

/** The v1 physical key, purged once on start. See LocalStorageKey.LastJobPath. */
const LEGACY_KEY = 'last_job_path';

/**
 * Remembers the last JOB the user was on, so the anonymous `/tsic` landing can return them
 * to it (authGuard's `redirectAuthenticated` arm).
 *
 * "Is this a job?" is answered by the ROUTER, not by inspecting the URL string. The previous
 * version took the first URL segment and screened it against a hardcoded list of six route
 * names — of which five (`error`, `unauthorized`, `login`, `register`, `privacy-policy`) are
 * not top-level routes at all, while the two that are (`forgot-password`, `reset-password`)
 * were missing. Visiting the forgot-password page therefore stored it as a job path, and the
 * next visit to `/tsic` redirected there. The `**` wildcard leaves the typed URL intact, so
 * any mistyped address did the same. A name list cannot track the route table; the
 * `:jobPath` param IS the route table's own answer.
 *
 * A navigation with no `:jobPath` (the `tsic` landing, password reset, a 404) simply leaves
 * the stored value alone — it does not change which job the user was last on. v1 cleared on
 * not-found only because it would otherwise have stored `not-found` AS a job.
 *
 * The write additionally requires the job to have been CONFIRMED to exist. jobPathMatch
 * already declines to bind a nonexistent job, so in practice a bogus path never reaches
 * NavigationEnd — but this is the write chokepoint, and the invariant ("only a real job is
 * ever stored") belongs here rather than resting on a guard somewhere else staying correct.
 * It also covers jobPathMatch's fail-open arm: when the existence check could not be
 * completed (API unreachable), the route is allowed through but the path is NOT persisted.
 */
@Injectable({ providedIn: 'root' })
export class LastLocationService {
    private readonly router = inject(Router);
    private readonly jobs = inject(JobService);

    constructor() {
        try { localStorage.removeItem(LEGACY_KEY); } catch { /* storage unavailable */ }

        this.router.events
            .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
            .subscribe(() => {
                // Deepest activated route, then the shared walk back up for :jobPath — the
                // same resolver the guards use, so "which job" has ONE definition app-wide.
                const jobPath = resolveJobPath(this.deepestActivatedRoute());
                if (jobPath && this.jobs.isKnownJob(jobPath)) {
                    localStorage.setItem(LocalStorageKey.LastJobPath, jobPath);
                }
            });
    }

    getLastJobPath(): string | null {
        return localStorage.getItem(LocalStorageKey.LastJobPath) || null;
    }

    /** Drop a stored path that no longer resolves. See authGuard's redirectAuthenticated arm. */
    clearLastJobPath(): void {
        try { localStorage.removeItem(LocalStorageKey.LastJobPath); } catch { /* storage unavailable */ }
    }

    private deepestActivatedRoute(): ActivatedRouteSnapshot {
        let r = this.router.routerState.snapshot.root;
        while (r.firstChild) r = r.firstChild;
        return r;
    }
}
