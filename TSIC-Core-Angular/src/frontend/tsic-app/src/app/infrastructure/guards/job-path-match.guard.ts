import { inject } from '@angular/core';
import { type CanMatchFn } from '@angular/router';
import { JobService } from '../services/job.service';

/**
 * Route-MATCH guard for the top-level `:jobPath` route.
 *
 * `:jobPath` is a bare parameter, so without this it binds ANY single URL segment. A
 * mistyped address like `/zzz-does-not-exist` therefore matched as a job, the `**` wildcard
 * below it became unreachable, and the app rendered job chrome for a job that does not
 * exist — a blank "Welcome" instead of a not-found page, because JobService.loadJobMetadata
 * swallows the 404. Worse, the navigation completed, so LastLocationService stored the
 * garbage as the user's last job and every later visit to the site root redirected to it.
 * (That poisoning is why LocalStorageKey.LastJobPath was already bumped to _v2 once; the
 * v2 write fixed the wrong-SEGMENT half but not the nonexistent-JOB half.)
 *
 * canMatch, not canActivate, is the right hook on purpose: declining a MATCH lets the
 * router fall through to `**` and render the real not-found page. A canActivate that
 * redirected would have to let the route activate first — chrome paints, NavigationEnd
 * fires, the storage write races the redirect.
 *
 * Segment 0 IS the candidate jobPath: this guard is only ever attached to a top-level
 * single-parameter route, and the router has already tokenized the URL, so reading it here
 * is not the URL-regex sniffing that navigation/job-path.ts warns against — there is no
 * ActivatedRouteSnapshot yet at match time, by design.
 *
 * Cost: one EXTRA round trip on the cold load of each distinct jobPath — it must not reuse
 * the metadata fetch, see the long note on JobService.jobExists. Memoized, so warm
 * navigations never re-ask. Fails OPEN if the API is unreachable.
 */
export const jobPathMatch: CanMatchFn = (_route, segments) => {
    const candidate = segments[0]?.path;
    if (!candidate) return false;

    const jobs = inject(JobService);

    // Fast path: already confirmed this session — stay synchronous so the router does not
    // take an async detour on every in-job navigation.
    if (jobs.isKnownJob(candidate)) return true;

    return jobs.jobExists(candidate);
};
