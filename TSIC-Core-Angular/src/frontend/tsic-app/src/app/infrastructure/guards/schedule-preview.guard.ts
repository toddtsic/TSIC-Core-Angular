import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { resolveJobPath } from '../navigation/job-path';
import { storeSchedulePreviewToken } from '../services/schedule-preview-token';

/**
 * Arriving from an emailed schedule-preview link (`#preview=<token>`): hold the token for this tab, drop it from
 * the address bar, and send an anonymous visitor through login (then role selection) back to the schedule.
 * The link itself needs no login; the preview does. Whether the invite is honored is decided server-side on
 * every request — this guard only carries the token to where that check can see it.
 * Without a preview fragment the public schedule route behaves exactly as before.
 */
export const schedulePreviewGuard: CanActivateFn = (route) => {
    const token = new URLSearchParams(route.fragment ?? '').get('preview');
    const jobPath = resolveJobPath(route);
    if (!token || !jobPath) return true;

    storeSchedulePreviewToken(token);
    const router = inject(Router);
    const schedule = `/${jobPath}/schedule`;
    return inject(AuthService).isAuthenticated()
        ? router.parseUrl(schedule)
        : router.createUrlTree([`/${jobPath}/login`], { queryParams: { returnUrl: schedule } });
};
