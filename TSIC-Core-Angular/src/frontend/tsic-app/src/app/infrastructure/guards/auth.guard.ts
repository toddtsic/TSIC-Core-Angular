import { inject } from '@angular/core';
import { Router, type CanActivateFn, type CanMatchFn } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';
import { LastLocationService } from '../services/last-location.service';
import { JobService } from '../services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import { type RoleName } from '../constants/roles.constants';
import { wasChunkRecoveryReload } from '../navigation/chunk-load-recovery';
import { resolveJobPath } from '../navigation/job-path';

/**
 * Unified authentication guard.
 *
 * Route data:
 *   allowAnonymous         – skip auth entirely (public pages, registration wizards)
 *   redirectAuthenticated  – bounce logged-in users away (login page, /tsic landing)
 *   roles: RoleName[]      – literal list of roles permitted on this route.
 *                            Reading the route shows exactly who can reach it.
 *                            Omit for any-authenticated-user access.
 *
 * Cold start (fresh load / refresh / externally-clicked deep link):
 *   Never resume a session — no role is exempt. logoutLocal(), then honor the requested URL
 *   (public/landing render anonymously). ONE mechanical exception: the chunk-load recovery's
 *   self-heal reload (deploy deleted a lazy chunk mid-click) keeps the session — identified by
 *   the recovery's own per-window sessionStorage stamp, see wasChunkRecoveryReload().
 *   Two protected-route sub-cases:
 *     • fresh anonymous deep link (no prior session) → login with returnUrl preserved, so an
 *       emailed invite is evaluated against whoever logs in FOR that link.
 *     • a LIVE session was discarded (user refreshed while working) → job home. The deep,
 *       role-gated returnUrl is dropped so re-login doesn't teleport back to e.g.
 *       search/registrations. Distinguished by the forced-logout marker on AuthService.
 */
export const authGuard: CanActivateFn = (route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const toast = inject(ToastService);
    const last = inject(LastLocationService);
    const jobs = inject(JobService);

    const user = auth.getCurrentUser();
    const isAuth = auth.isAuthenticated();
    const isColdStart = !router.navigated;

    // The forced-logout marker only applies to the very first (cold-start) navigation. Any
    // warm navigation renders it stale — clear it so a deliberate protected-link click still
    // bounces to login with its returnUrl preserved (the parent :jobPath guard runs before any
    // protected child, so this clears ahead of the not-authenticated check below).
    if (!isColdStart) auth.clearForcedColdStartLogout();

    const flags = {
        allowAnonymous: route.data['allowAnonymous'] === true,
        redirectAuthenticated: route.data['redirectAuthenticated'] === true,
    };
    const allowedRoles = route.data['roles'] as RoleName[] | undefined;

    // ── Helpers ──────────────────────────────────────────────────────
    // 'tsic' is the correct default here, not a guess: this guard also runs on the literal
    // `tsic` route, which has no :jobPath param at all. Elsewhere the walk always resolves.
    const jobPath = (): string => resolveJobPath(route) || 'tsic';

    const toRoleSelection = () => router.createUrlTree([`/${jobPath()}/role-selection`]);
    const toLogin = () => router.createUrlTree([`/${jobPath()}/login`], { queryParams: { returnUrl: state.url } });
    const toJob = (jp: string) => router.createUrlTree([`/${jp}`]);

    // ── Cold start = never resume a session ─────────────────────────
    // App startup (fresh load, refresh, or an externally-clicked deep link such as an
    // emailed invite) must begin clean. A link is then judged against whoever logs in
    // *for that link* — never against whoever happened to be left in the browser. NO role
    // is exempt: the former SuperUser/Director "resume across refresh" carve-out is exactly
    // what let a privileged token evaluate (and reject) someone else's invite, so it's gone.
    //
    // Gate on ANY session material — a live token OR just a refresh token. Gating on isAuth
    // alone would skip an expired-but-refreshable session, which the not-authenticated block
    // below would then silently refresh back into the OLD user, quietly undoing this. Running
    // logoutLocal() first (it clears both tokens and stops the refresh timer) closes that path.
    //
    // Then honor the URL that was actually requested — do NOT redirect home. Public and
    // login/landing pages render anonymously; anything protected lands the user on the job
    // HOME (see below) rather than round-tripping the deep URL through login.
    if (isColdStart && (isAuth || auth.getRefreshToken())) {
        // Exception: the chunk-load recovery's OWN self-heal reload (a deploy deleted a lazy
        // chunk mid-session → auto full reload to fetch fresh hashes). That is the same user,
        // mid-click — not a fresh visit — so discarding their session here is exactly the
        // "click a menu item, get thrown to login" bug. The recovery stamps per-window
        // sessionStorage right before reloading (a genuine fresh visit / emailed invite link
        // never carries the stamp, so the invite-link security rationale is untouched); within
        // its 15s window we keep the session and fall through to normal warm-equivalent
        // handling below. Read-only check — see wasChunkRecoveryReload() for why the stamp is
        // never cleared (it doubles as the reload-loop brake, and every guard run on this
        // navigation must see the same answer).
        if (!wasChunkRecoveryReload()) {
            auth.logoutLocal();
            // A LIVE session was just discarded on this cold start (a fresh anonymous deep-link
            // click has no session and never reaches here). Mark it so a role-gated child guard
            // sends the user to the job home instead of preserving the deep, role-gated returnUrl
            // — which would teleport the re-login back to e.g. search/registrations.
            auth.markForcedColdStartLogout();
            return (flags.allowAnonymous || flags.redirectAuthenticated) ? true : toJob(jobPath());
        }
    }

    // ── Bounce authenticated users away from login/landing ──────────
    if (flags.redirectAuthenticated) {
        if (!isAuth) {
            const force = route.queryParamMap.get('force');
            if (force === '1' || force === 'true'
                || route.queryParamMap.has('returnUrl')
                || route.queryParamMap.has('intent')) {
                return true;
            }
            const lastJob = last.getLastJobPath();
            if (!lastJob) return true;

            // Validate before bouncing. A stored path can be stale (job deleted) or poisoned
            // by the pre-jobPathMatch writer, and redirecting to one now lands on the
            // not-found page — so the SITE ROOT would 404 forever for that browser, with no
            // way out but clearing storage by hand. Confirm it, and drop the key on a miss so
            // an already-poisoned browser self-heals on its next visit. Memoized in
            // JobService: this costs a round trip once per session, on this arm only.
            return jobs.jobExists(lastJob).pipe(
                map(exists => {
                    if (exists) return toJob(lastJob);
                    last.clearLastJobPath();
                    return true;
                })
            );
        }

        const returnUrl = route.queryParamMap.get('returnUrl');
        if (returnUrl) {
            try {
                const u = new URL(returnUrl, globalThis.location.origin);
                if (u.origin === globalThis.location.origin) {
                    return router.parseUrl(`${u.pathname}${u.search}${u.hash}`);
                }
            } catch { /* malformed → fall through */ }
        }

        return user?.regId && user.jobPath
            ? toJob(user.jobPath)
            : toRoleSelection();
    }

    // ── Not authenticated ────────────────────────────────────────────
    if (!user || !isAuth) {
        if (flags.allowAnonymous) return true;

        // Forced cold-start logout of a live session → don't round-trip this role-gated URL
        // through login (that teleports the re-login back to e.g. search/registrations). Land
        // on the job home; the user logs in from the header when ready. A genuinely anonymous
        // deep-link click (no prior session) is never marked, so its returnUrl is preserved.
        if (auth.wasForcedColdStartLogout()) return toJob(jobPath());

        const refreshToken = auth.getRefreshToken();
        const regId = user?.regId;
        auth.logoutLocal();

        if (refreshToken) {
            return auth.refreshAccessToken(refreshToken, regId).pipe(
                map(() => true),
                catchError(() => [toLogin()])
            );
        }

        return toLogin();
    }

    // ── jobPath mismatch ─────────────────────────────────────────────
    const urlJob = jobPath();
    if (urlJob && user.jobPath && urlJob !== user.jobPath) {
        if (urlJob === 'tsic' && user.jobPath !== 'tsic') {
            return toJob(user.jobPath);
        }
        if (state.url.includes('/role-selection')) {
            return true; // allow cross-job role switching
        }
        // Silent bounce home. This is warm-navigation only (cold start force-logs-off
        // above); a URL-bar edit reloads → cold start, and mandated-relative routerLinks
        // can't cross jobs — so the only thing landing here is a stray internal nav the
        // user never intended. No toast: there's nothing for them to act on.
        return toJob(user.jobPath);
    }

    // ── Role-set gating ──────────────────────────────────────────────
    // Route declares which concrete roles may access it via data.roles.
    // No array → any authenticated user is fine.
    if (allowedRoles && allowedRoles.length > 0) {
        if (!user.jobPath) return toRoleSelection();
        const userRoles = user.roles ?? (user.role ? [user.role] : []);
        const ok = allowedRoles.some(r => userRoles.includes(r));
        if (!ok) {
            toast.show('Access denied.', 'danger');
            return router.createUrlTree([`/${user.jobPath}`, 'home']);
        }
    }

    return true;
};

/**
 * Match guard for the standalone /tsic marketing landing.
 * Returns false (route does not match) when the user has selected a role,
 * so authenticated Phase 2 users fall through to :jobPath and see their
 * workspace at /tsic instead of the corporate landing page.
 *
 * Cold start is exempt: canMatch runs BEFORE canActivate, so on a fresh load a
 * leftover Phase-2 token still reads as hasSelectedRole()===true here — which used
 * to decline the marketing landing and fall through to :jobPath, only for authGuard's
 * cold-start "never resume" block to then logoutLocal() and render job-landing
 * anonymously (the "came up logged in, logged out, screen never changed" bug). On cold
 * start the session is always discarded, so treat the user as anonymous and MATCH the
 * corporate landing — consistent with authGuard's own isColdStart handling.
 */
export const unselectedRoleMatch: CanMatchFn = () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!router.navigated) return true;
    return !auth.hasSelectedRole();
};
