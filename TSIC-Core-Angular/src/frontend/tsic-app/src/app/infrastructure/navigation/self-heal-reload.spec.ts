import { selfHealReload } from './self-heal-reload';

/**
 * Two invariants:
 *   1. The reload is a RELOAD (location.reload) preceded by replaceState(target) — that exact
 *      pair is what makes the browser report "reload" and lets auth.guard keep the session
 *      while still landing on the URL the user was heading to.
 *   2. The brake: no second reload within the window; sessionStorage-scoped so it is per-tab.
 */
describe('selfHealReload', () => {
    let perform: ReturnType<typeof vi.spyOn>;

    beforeEach(() => {
        sessionStorage.clear();
        perform = vi.spyOn(selfHealReload, 'perform').mockImplementation(() => undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
        vi.useRealTimers();
        sessionStorage.clear();
    });

    it('performs the reload and reports true when not braked', () => {
        expect(selfHealReload.request('/job/registration/team')).toBe(true);
        expect(perform).toHaveBeenCalledWith('/job/registration/team');
        expect(selfHealReload.recentlyReloaded()).toBe(true);
    });

    it('refuses a second reload inside the brake window', () => {
        expect(selfHealReload.request()).toBe(true);
        expect(selfHealReload.request()).toBe(false);
        expect(perform).toHaveBeenCalledTimes(1);
    });

    it('allows a reload again once the brake window has passed', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-08-17T12:00:00Z'));
        expect(selfHealReload.request()).toBe(true);
        vi.setSystemTime(new Date('2026-08-17T12:00:16Z')); // > 15s later
        expect(selfHealReload.recentlyReloaded()).toBe(false);
        expect(selfHealReload.request()).toBe(true);
        expect(perform).toHaveBeenCalledTimes(2);
    });

    it('brake lives in sessionStorage (per-tab), not localStorage (cross-tab)', () => {
        selfHealReload.request();
        expect(sessionStorage.getItem('self-heal-reload-at')).toBeTruthy();
        expect(localStorage.getItem('self-heal-reload-at')).toBeNull();
    });

    describe('perform (the real browser call)', () => {
        it('replaceState(target) then location.reload — never location.assign', () => {
            perform.mockRestore();
            const replaceState = vi.spyOn(history, 'replaceState').mockImplementation(() => undefined);
            // jsdom's location.reload is non-configurable; swap the whole location object
            // for the duration so we can observe the call.
            const reload = vi.fn();
            const assign = vi.fn();
            vi.stubGlobal('location', { ...globalThis.location, reload, assign });
            try {
                selfHealReload.perform('/next/screen?x=1');
                expect(replaceState).toHaveBeenCalledWith(history.state, '', '/next/screen?x=1');
                expect(reload).toHaveBeenCalledTimes(1);
                expect(assign).not.toHaveBeenCalled();
            } finally {
                vi.unstubAllGlobals();
            }
        });

        it('with no target, reloads in place without touching history', () => {
            perform.mockRestore();
            const replaceState = vi.spyOn(history, 'replaceState').mockImplementation(() => undefined);
            const reload = vi.fn();
            vi.stubGlobal('location', { ...globalThis.location, reload });
            try {
                selfHealReload.perform();
                expect(replaceState).not.toHaveBeenCalled();
                expect(reload).toHaveBeenCalledTimes(1);
            } finally {
                vi.unstubAllGlobals();
            }
        });
    });
});
