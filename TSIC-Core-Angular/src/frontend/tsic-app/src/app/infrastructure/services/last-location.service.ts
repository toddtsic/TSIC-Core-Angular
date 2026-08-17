import { Injectable, inject } from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, map, distinctUntilChanged } from 'rxjs/operators';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { JobService } from '@infrastructure/services/job.service';

/** The v1 physical key, purged once on start. See LocalStorageKey.LastJobPath. */
const LEGACY_KEY = 'last_job_path';

/**
 * Remembers the last JOB the user was on, so the anonymous `/tsic` landing can return them
 * to it (authGuard's `redirectAuthenticated` arm).
 *
 * "Is this a job?" is answered by the SERVER, not by the URL and not by the router. Two
 * earlier versions got this wrong in the same way, one segment at a time:
 *   v1 took the first URL segment and screened it against a hardcoded list of six route
 *     names, so `forgot-password` and every mistyped address were stored as job paths.
 *   v2 asked the router for its `:jobPath` param instead — correct about which SEGMENT is
 *     the job, still silent about whether that job EXISTS, because `:jobPath` binds anything.
 *
 * This version writes only when job metadata actually came back, which is the only evidence
 * that settles the question. It hangs off `JobService.currentJob` rather than `NavigationEnd`
 * for exactly that reason: at NavigationEnd the fetch is still on the wire, so gating a
 * navigation-time write on "do we have metadata yet" would never fire for a real job either.
 *
 * `toObservable` + `subscribe`, not `effect()` — the sanctioned way to react to a service
 * signal here. See .claude/rules/frontend-angular.md.
 */
@Injectable({ providedIn: 'root' })
export class LastLocationService {
    private readonly jobs = inject(JobService);

    constructor() {
        try { localStorage.removeItem(LEGACY_KEY); } catch { /* storage unavailable */ }

        // Confirmed job → remember it. jobPath comes off the response, so it carries the
        // backend's own casing rather than whatever the user typed.
        //
        // distinctUntilChanged is not cosmetic: currentJob is deliberately re-set to a NEW
        // object for the same job on return visits (that fresh reference is what re-fires the
        // pulse — see JobService.requestJobMetadata), so without it every such refetch rewrites
        // the identical string to localStorage. Map to the path first so the comparison is on
        // the value, not the object identity.
        toObservable(this.jobs.currentJob)
            .pipe(
                map(job => job?.jobPath ?? null),
                filter((jobPath): jobPath is string => !!jobPath),
                distinctUntilChanged(),
                takeUntilDestroyed()
            )
            .subscribe(jobPath => {
                try { localStorage.setItem(LocalStorageKey.LastJobPath, jobPath); }
                catch { /* storage unavailable */ }
            });

        // A job came back 404. If it is the one we have stored, drop it — otherwise this
        // browser redirects to a dead job from the site root on every future visit, which is
        // how the v1 poisoning became permanent for real users.
        toObservable(this.jobs.jobNotFound)
            .pipe(filter((p): p is string => !!p), takeUntilDestroyed())
            .subscribe(missingPath => {
                const stored = this.getLastJobPath();
                if (stored && stored.toLowerCase() === missingPath.toLowerCase()) {
                    this.clearLastJobPath();
                }
            });
    }

    getLastJobPath(): string | null {
        return localStorage.getItem(LocalStorageKey.LastJobPath) || null;
    }

    clearLastJobPath(): void {
        try { localStorage.removeItem(LocalStorageKey.LastJobPath); } catch { /* storage unavailable */ }
    }
}
