import type { ActivatedRouteSnapshot } from '@angular/router';

/**
 * The jobPath for a route, from the router's own parse. ONE definition, because four
 * hand-copies of this drifted and the stale ones mis-identified the job.
 *
 * Walk the FULL ancestor chain. The router runs `paramsInheritanceStrategy: 'emptyOnly'`
 * and `:jobPath` carries a component (LayoutComponent), so inheritance STOPS there: a route
 * two levels down (`:jobPath/registration/team`, `:jobPath/configure/job`) sees `{}` on both
 * itself AND its parent, because the component-less middle route inherited nothing to pass
 * on. A `route.paramMap || route.parent?.paramMap` lookup therefore resolves NOTHING on
 * those routes — not occasionally, every time.
 *
 * There is deliberately no URL-string fallback. The router already parsed the URL; re-parsing
 * it with a regex is how this broke — the old `/^\/([a-z0-9-]{3,40})/` rejected the uppercase
 * and >40-char paths that `Jobs.JobPath varchar(80)` permits, callers defaulted to the house
 * job, and registration guards then evaluated the WRONG JOB's pulse and bounced registrants
 * off their own open event. On a route with no `:jobPath` ancestor such a regex is worse than
 * useless: it would return `forgot-password` as a job name.
 *
 * Returns null when this route genuinely has no job. Callers decide what that means — a guard
 * that cannot name the job must not gate on the job.
 */
export function resolveJobPath(route: ActivatedRouteSnapshot | null): string | null {
    let r = route;
    while (r) {
        const jp = r.paramMap.get('jobPath');
        if (jp) return jp;
        r = r.parent;
    }
    return null;
}
