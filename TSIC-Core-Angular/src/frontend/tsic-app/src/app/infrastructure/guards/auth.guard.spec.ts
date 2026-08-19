import { TestBed } from '@angular/core/testing';
import {
    Router, UrlTree, convertToParamMap,
    type ActivatedRouteSnapshot, type RouterStateSnapshot,
} from '@angular/router';
import { authGuard, unselectedRoleMatch } from './auth.guard';
import { AuthService } from '../services/auth.service';
import { LastLocationService } from '../services/last-location.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * The cold-start session rule, both halves, side by side:
 *   ARRIVED AT from outside (emailed invite, typed URL, new tab) → session discarded.
 *   RELOADED (app self-heal for a new build / missing chunk, or F5) → session kept.
 * Plus: warm navigation is untouched by either.
 *
 * The load kind is driven through the SAME browser API the real wasPageReloaded() reads
 * (performance.getEntriesByType('navigation')), so these run the guard end-to-end — no
 * module spying.
 */

/** Make the browser report how this document was opened. */
function pageOpenedBy(type: 'reload' | 'navigate' | 'back_forward'): ReturnType<typeof vi.spyOn> {
    return vi.spyOn(performance, 'getEntriesByType')
        .mockReturnValue([{ type } as unknown as PerformanceNavigationTiming]);
}

describe('authGuard — cold-start session rule', () => {
    let auth: {
        getCurrentUser: ReturnType<typeof vi.fn>;
        isAuthenticated: ReturnType<typeof vi.fn>;
        getRefreshToken: ReturnType<typeof vi.fn>;
        logoutLocal: ReturnType<typeof vi.fn>;
        markForcedColdStartLogout: ReturnType<typeof vi.fn>;
        wasForcedColdStartLogout: ReturnType<typeof vi.fn>;
        clearForcedColdStartLogout: ReturnType<typeof vi.fn>;
        hasSelectedRole: ReturnType<typeof vi.fn>;
        refreshAccessToken: ReturnType<typeof vi.fn>;
    };
    let router: Router;

    const user = { username: 'director', regId: 'r1', jobPath: 'aim-players', roles: ['Director'] };

    /** A protected route under :jobPath (no allowAnonymous, no roles list). */
    const route = (data: Record<string, unknown> = {}, jobPath = 'aim-players') => {
        const parent = {
            paramMap: convertToParamMap({ jobPath }),
            parent: null,
            data: {},
            queryParamMap: convertToParamMap({}),
        } as unknown as ActivatedRouteSnapshot;
        return {
            paramMap: convertToParamMap({}),
            parent,
            data,
            queryParamMap: convertToParamMap({}),
        } as unknown as ActivatedRouteSnapshot;
    };
    const state = (url: string) => ({ url }) as RouterStateSnapshot;

    const run = (r: ActivatedRouteSnapshot, s: RouterStateSnapshot) =>
        TestBed.runInInjectionContext(() => authGuard(r, s));

    beforeEach(() => {
        auth = {
            getCurrentUser: vi.fn().mockReturnValue(user),
            isAuthenticated: vi.fn().mockReturnValue(true),
            getRefreshToken: vi.fn().mockReturnValue('refresh-token'),
            logoutLocal: vi.fn(),
            markForcedColdStartLogout: vi.fn(),
            wasForcedColdStartLogout: vi.fn().mockReturnValue(false),
            clearForcedColdStartLogout: vi.fn(),
            hasSelectedRole: vi.fn().mockReturnValue(true),
            refreshAccessToken: vi.fn(),
        };
        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: auth },
                { provide: LastLocationService, useValue: { getLastJobPath: () => null } },
                { provide: ToastService, useValue: { show: vi.fn() } },
            ],
        });
        router = TestBed.inject(Router);
    });

    afterEach(() => vi.restoreAllMocks());

    describe('cold start (router has not navigated yet)', () => {
        beforeEach(() => { (router as { navigated: boolean }).navigated = false; });

        it('ARRIVED AT from outside with a live session → session discarded, sent to job home', () => {
            pageOpenedBy('navigate');
            const result = run(route(), state('/aim-players/search/registrations'));
            expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
            expect(auth.markForcedColdStartLogout).toHaveBeenCalledTimes(1);
            expect(result).toBeInstanceOf(UrlTree);
            expect(router.serializeUrl(result as UrlTree)).toBe('/aim-players');
        });

        it('ARRIVED AT via back/forward from another site → same: session discarded', () => {
            pageOpenedBy('back_forward');
            run(route(), state('/aim-players/search/registrations'));
            expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
        });

        it('ARRIVED AT with only a refresh token (expired session) → still discarded, not silently refreshed', () => {
            pageOpenedBy('navigate');
            auth.isAuthenticated.mockReturnValue(false);
            auth.getCurrentUser.mockReturnValue(null);
            run(route(), state('/aim-players/search/registrations'));
            expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
            expect(auth.refreshAccessToken).not.toHaveBeenCalled();
        });

        it('ARRIVED AT on a public/anonymous route → session discarded, route still allowed', () => {
            pageOpenedBy('navigate');
            const result = run(route({ allowAnonymous: true }), state('/aim-players/schedule'));
            expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
            expect(result).toBe(true);
        });

        it('RELOADED (self-heal for a new build / missing chunk, or F5) → session KEPT, route allowed', () => {
            pageOpenedBy('reload');
            const result = run(route(), state('/aim-players/search/registrations'));
            expect(auth.logoutLocal).not.toHaveBeenCalled();
            expect(auth.markForcedColdStartLogout).not.toHaveBeenCalled();
            expect(result).toBe(true);
        });

        it('RELOADED with no session at all → behaves like any anonymous protected hit (to login)', () => {
            pageOpenedBy('reload');
            auth.isAuthenticated.mockReturnValue(false);
            auth.getCurrentUser.mockReturnValue(null);
            auth.getRefreshToken.mockReturnValue(null);
            const result = run(route(), state('/aim-players/search/registrations'));
            expect(result).toBeInstanceOf(UrlTree);
            expect(router.serializeUrl(result as UrlTree)).toContain('/aim-players/login');
        });

        it('every guard run on the same cold start gets the same answer (parent then child)', () => {
            pageOpenedBy('reload');
            expect(run(route(), state('/aim-players'))).toBe(true);
            expect(run(route(), state('/aim-players/search/registrations'))).toBe(true);
            expect(auth.logoutLocal).not.toHaveBeenCalled();
        });

        it('browser cannot say how the page opened → treated as ARRIVED AT (fail closed)', () => {
            vi.spyOn(performance, 'getEntriesByType').mockReturnValue([]);
            Object.defineProperty(performance, 'navigation', { value: undefined, configurable: true });
            try {
                run(route(), state('/aim-players/search/registrations'));
                expect(auth.logoutLocal).toHaveBeenCalledTimes(1);
            } finally {
                delete (performance as unknown as { navigation?: unknown }).navigation;
            }
        });
    });

    describe('warm navigation (in-app click)', () => {
        beforeEach(() => { (router as { navigated: boolean }).navigated = true; });

        it('is untouched by the load kind — never logs out, never consults it', () => {
            const spy = pageOpenedBy('navigate');
            const result = run(route(), state('/aim-players/search/registrations'));
            expect(auth.logoutLocal).not.toHaveBeenCalled();
            expect(result).toBe(true);
            expect(spy).not.toHaveBeenCalled();
        });
    });
});

describe('unselectedRoleMatch — /tsic marketing landing', () => {
    let router: Router;
    let hasSelectedRole: ReturnType<typeof vi.fn>;

    const run = () => TestBed.runInInjectionContext(() =>
        unselectedRoleMatch({} as never, [] as never, {} as never));

    beforeEach(() => {
        hasSelectedRole = vi.fn().mockReturnValue(true);
        TestBed.configureTestingModule({
            providers: [{ provide: AuthService, useValue: { hasSelectedRole } }],
        });
        router = TestBed.inject(Router);
    });
    afterEach(() => vi.restoreAllMocks());

    it('cold start ARRIVED AT → matches the landing (authGuard is about to discard the session)', () => {
        (router as { navigated: boolean }).navigated = false;
        pageOpenedBy('navigate');
        expect(run()).toBe(true);
    });

    it('cold start RELOADED with a role selected → does NOT match; falls through to the workspace', () => {
        (router as { navigated: boolean }).navigated = false;
        pageOpenedBy('reload');
        expect(run()).toBe(false);
    });

    it('warm, role selected → does not match', () => {
        (router as { navigated: boolean }).navigated = true;
        expect(run()).toBe(false);
    });

    it('warm, no role → matches', () => {
        (router as { navigated: boolean }).navigated = true;
        hasSelectedRole.mockReturnValue(false);
        expect(run()).toBe(true);
    });
});

/**
 * The site-root hang. `/tsic` is itself a `redirectAuthenticated` route, so a stored last job
 * of 'tsic' made this arm redirect /tsic → /tsic without end. `onSameUrlNavigation: 'ignore'`
 * cannot break it on a cold start: Angular's skip predicate leads with `!router.navigated`,
 * which stays false for as long as no navigation has COMPLETED. Reproduced 2026-08-19 — one
 * localStorage key in a clean Incognito profile wedged the app at the site root: blank page,
 * main thread pegged, no console error, no request. Cost the better part of a day to find.
 */
describe('authGuard — redirectAuthenticated never self-redirects', () => {
    let router: Router;
    let lastJobPath: string | null;

    const anonymous = {
        getCurrentUser: () => null,
        isAuthenticated: () => false,
        getRefreshToken: () => null,
        logoutLocal: vi.fn(),
        markForcedColdStartLogout: vi.fn(),
        wasForcedColdStartLogout: () => false,
        clearForcedColdStartLogout: vi.fn(),
        hasSelectedRole: () => false,
        refreshAccessToken: vi.fn(),
    };

    /** A landing/login route carrying redirectAuthenticated. */
    const landing = (jobPath: string, url: string) => ({
        route: {
            paramMap: convertToParamMap({ jobPath }),
            parent: null,
            data: { redirectAuthenticated: true },
            queryParamMap: convertToParamMap({}),
        } as unknown as ActivatedRouteSnapshot,
        state: { url } as RouterStateSnapshot,
    });

    const run = (r: ActivatedRouteSnapshot, s: RouterStateSnapshot) =>
        TestBed.runInInjectionContext(() => authGuard(r, s));

    beforeEach(() => {
        lastJobPath = null;
        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: anonymous },
                { provide: LastLocationService, useValue: { getLastJobPath: () => lastJobPath } },
                { provide: ToastService, useValue: { show: vi.fn() } },
            ],
        });
        router = TestBed.inject(Router);
        (router as { navigated: boolean }).navigated = true;
    });

    afterEach(() => vi.restoreAllMocks());

    it('COLD START at the site root with the house job stored → stands down, no loop', () => {
        pageOpenedBy('navigate');
        (router as { navigated: boolean }).navigated = false;
        lastJobPath = 'tsic';
        const { route, state } = landing('tsic', '/tsic');
        expect(run(route, state)).toBe(true);
    });

    it('stored last job IS the page we are on → stands down', () => {
        lastJobPath = 'tsic';
        const { route, state } = landing('tsic', '/tsic');
        expect(run(route, state)).toBe(true);
    });

    it('stored last job is a DIFFERENT job → still redirects to it', () => {
        lastJobPath = 'aim-players';
        const { route, state } = landing('tsic', '/tsic');
        const result = run(route, state);
        expect(result).toBeInstanceOf(UrlTree);
        expect(router.serializeUrl(result as UrlTree)).toBe('/aim-players');
    });

    it('/{job}/login with that same job stored → unchanged, still bounces to the job landing', () => {
        lastJobPath = 'aim-players';
        const { route, state } = landing('aim-players', '/aim-players/login');
        const result = run(route, state);
        expect(result).toBeInstanceOf(UrlTree);
        expect(router.serializeUrl(result as UrlTree)).toBe('/aim-players');
    });
});
