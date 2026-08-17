import { TestBed } from '@angular/core/testing';
import { CrossTabSessionSyncService } from './cross-tab-session-sync.service';
import { AuthService } from './auth.service';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { selfHealReload } from '../navigation/self-heal-reload';
import { decodeJwtPayload } from '../utils/jwt-payload';

/** Unsigned JWT with the given payload — the service only reads claims, never verifies. */
function jwt(payload: Record<string, unknown>): string {
    const b64url = (s: string) => btoa(s).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
    return `${b64url('{"alg":"none"}')}.${b64url(JSON.stringify(payload))}.sig`;
}

const TODD_DIRECTOR = { username: 'todd', regId: 'r-dir', jobPath: 'lftc-summer-2027' };
const TODD_CLUBREP = { username: 'todd', regId: 'r-club', jobPath: 'lftc-summer-2027' };
const ANN = { username: 'ann', regId: 'r-ann', jobPath: 'aim-players' };

describe('decodeJwtPayload', () => {
    it('reads claims from a well-formed token', () => {
        expect(decodeJwtPayload(jwt({ sub: 'x', regId: 'r' }))).toEqual({ sub: 'x', regId: 'r' });
    });
    it('null for junk', () => {
        expect(decodeJwtPayload(null)).toBeNull();
        expect(decodeJwtPayload('')).toBeNull();
        expect(decodeJwtPayload('not-a-token')).toBeNull();
        expect(decodeJwtPayload('a.!!!.c')).toBeNull();
    });
});

/**
 * "One browser, one session; every tab mirrors it."
 * These drive the SAME browser event the real thing listens for (`storage` on window).
 */
describe('CrossTabSessionSyncService', () => {
    let stored: string | null;               // what localStorage now holds (the OTHER tab wrote it)
    let shown: Record<string, unknown> | null; // what THIS tab is rendering
    let request: ReturnType<typeof vi.spyOn>;
    let service: CrossTabSessionSyncService;

    /** Another tab changed the auth token: fire what the browser fires in THIS tab. */
    const otherTabWrote = (newValue: string | null, key: string | null = LocalStorageKey.AuthToken) => {
        stored = newValue;
        window.dispatchEvent(new StorageEvent('storage', { key, newValue, storageArea: localStorage }));
    };

    beforeEach(() => {
        stored = null;
        shown = null;
        request = vi.spyOn(selfHealReload, 'request').mockReturnValue(true);
        TestBed.configureTestingModule({
            providers: [{
                provide: AuthService,
                useValue: {
                    getToken: () => stored,
                    getCurrentUser: () => shown,
                },
            }],
        });
        service = TestBed.inject(CrossTabSessionSyncService);
        service.start();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('other tab logged out while this tab shows a user → reload', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(null);
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('other tab cleared ALL of localStorage while this tab shows a user → reload', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(null, null);
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('other tab logged in as a DIFFERENT user → reload', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(jwt(ANN));
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('other tab switched ROLE (same user, different regId) → reload', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(jwt(TODD_CLUBREP));
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('other tab logged in while this tab is anonymous → reload (this tab becomes that user)', () => {
        shown = null;
        otherTabWrote(jwt(TODD_DIRECTOR));
        expect(request).toHaveBeenCalledTimes(1);
    });

    it('routine token REFRESH — same user, role, job, new token → NO reload', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(jwt({ ...TODD_DIRECTOR, exp: 9_999_999_999, jti: 'fresh' }));
        expect(request).not.toHaveBeenCalled();
    });

    describe('login in another tab is TWO writes; only the settled one may touch this tab', () => {
        const PHASE_1 = jwt({ sub: 'todd' });               // credentials accepted, no role yet
        const PHASE_2 = jwt(TODD_DIRECTOR);                 // role chosen

        it('Phase-1 token written while this tab is anonymous → ignored (mid-login elsewhere)', () => {
            shown = null;
            otherTabWrote(PHASE_1);
            expect(request).not.toHaveBeenCalled();
        });

        it('Phase-1 token written while this tab shows a settled user → still ignored, wait for the role', () => {
            shown = ANN;
            otherTabWrote(PHASE_1);
            expect(request).not.toHaveBeenCalled();
        });

        it('SCENARIO (Todd, tab 2 logs in while tab 1 is anonymous): reload exactly once, on role selection', () => {
            shown = null;
            otherTabWrote(PHASE_1);                          // credentials submitted
            otherTabWrote(jwt({ sub: 'todd', jti: 'r2' }));  // even a refreshed Phase-1
            expect(request).not.toHaveBeenCalled();
            otherTabWrote(PHASE_2);                          // role chosen
            expect(request).toHaveBeenCalledTimes(1);
        });

        it('this tab is itself mid role-selection and the other tab logs out → catch up (reload)', () => {
            shown = { username: 'todd' };                    // Phase-1 shown here
            otherTabWrote(null);
            expect(request).toHaveBeenCalledTimes(1);
        });

        it('this tab is mid role-selection and the other tab completes login → catch up (reload)', () => {
            shown = { username: 'todd' };
            otherTabWrote(PHASE_2);
            expect(request).toHaveBeenCalledTimes(1);
        });
    });

    it('both anonymous → NO reload', () => {
        shown = null;
        otherTabWrote(null);
        expect(request).not.toHaveBeenCalled();
    });

    it('unrelated localStorage key → ignored', () => {
        shown = TODD_DIRECTOR;
        otherTabWrote(null, LocalStorageKey.AppTheme);
        expect(request).not.toHaveBeenCalled();
    });

    it('braked (self-heal reload seconds ago) → waits out the brake and looks again', () => {
        vi.useFakeTimers();
        request.mockReturnValueOnce(false).mockReturnValueOnce(true);
        shown = TODD_DIRECTOR;
        otherTabWrote(null);
        expect(request).toHaveBeenCalledTimes(1);   // refused
        vi.advanceTimersByTime(15_000);
        expect(request).toHaveBeenCalledTimes(2);   // retried, storage still disagrees
    });

    it('braked, but the session was restored before the retry → no reload', () => {
        vi.useFakeTimers();
        request.mockReturnValueOnce(false);
        shown = TODD_DIRECTOR;
        otherTabWrote(null);                       // refused, retry armed
        stored = jwt(TODD_DIRECTOR);               // other tab logged back in as the same user
        vi.advanceTimersByTime(15_000);
        expect(request).toHaveBeenCalledTimes(1);  // the retry found agreement
    });

    it('start() is idempotent — one listener', () => {
        service.start();
        shown = TODD_DIRECTOR;
        otherTabWrote(null);
        expect(request).toHaveBeenCalledTimes(1);
    });
});
