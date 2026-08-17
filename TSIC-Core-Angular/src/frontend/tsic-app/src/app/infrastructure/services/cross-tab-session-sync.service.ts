import { DestroyRef, Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { decodeJwtPayload } from '../utils/jwt-payload';
import { SELF_HEAL_BRAKE_MS, selfHealReload } from '../navigation/self-heal-reload';

/**
 * One browser, one session; every tab mirrors it.
 *
 * Auth tokens live in localStorage, which every tab of the site shares. When another tab
 * logs out (or the auth guard discards the session because a link was opened from outside),
 * this tab's tokens vanish underneath it — but its screen is in memory and keeps showing the
 * logged-in user until something hits the guard or the API. A tab that shows a user it no
 * longer is, is lying. Todd's rule: it must catch up, not pretend.
 *
 * The browser fires a `storage` event in every OTHER tab whenever localStorage changes.
 * We listen for the auth-token key and compare the session identity now in storage with the
 * one this tab is rendering. Different (gone, or a different user / role / job) → full reload
 * onto the current URL; the reload keeps whatever the store now holds (see page-load-kind.ts:
 * a reload keeps the session), so this tab becomes whatever the browser's session is —
 * logged out to the login page, or the other user, or the newly selected role.
 *
 * Same identity (the routine access-token refresh every N minutes rewrites the key with a
 * new token for the same user) → nothing. That is the one case that must NOT reload, or every
 * background refresh would blank every other tab.
 *
 * TRANSITIONAL state is ignored. Login is two phases: credentials → a Phase-1 token
 * (username only, no regId), then role selection → the Phase-2 token that actually says who
 * the user is here. Mirroring the Phase-1 token was a ping-pong: tab 2 submits credentials →
 * tab 1 reloads onto a role-less token → its API calls 401 → refresh without a regId is
 * rejected → logout() → tokens removed → tab 2 reloads mid role-selection and lands on /tsic.
 * So a stored Phase-1 token means "another tab is mid-login": do nothing, wait for the settled
 * state. A login elsewhere touches this tab exactly once — when the role is chosen.
 *
 * Nothing is stored, no polling. No loop is possible: this tab's reload writes nothing to
 * localStorage, so it never echoes back to the tab that changed it.
 */

/** The three claims that say WHO this session is. Any change = a different session. */
interface SessionIdentity {
    username: string;
    regId: string;
    jobPath: string | null;
}

/** What storage says the session is: settled identity, nobody, or mid-login (Phase-1). */
type StoredSession = SessionIdentity | 'anonymous' | 'transitional';

function storedSession(token: string | null): StoredSession {
    const p = decodeJwtPayload(token);
    if (!p) return 'anonymous';
    const username = (p['username'] ?? p['sub']) as string | undefined;
    if (!username) return 'anonymous';
    const regId = p['regId'] as string | undefined;
    if (!regId) return 'transitional';
    return { username, regId, jobPath: (p['jobPath'] as string | undefined) ?? null };
}

function sameIdentity(a: SessionIdentity | null, b: SessionIdentity | null): boolean {
    if (!a && !b) return true;
    if (!a || !b) return false;
    return a.username === b.username && a.regId === b.regId && a.jobPath === b.jobPath;
}

@Injectable({ providedIn: 'root' })
export class CrossTabSessionSyncService {
    private readonly auth = inject(AuthService);
    private readonly destroyRef = inject(DestroyRef);
    private started = false;
    private retryHandle: ReturnType<typeof setTimeout> | null = null;

    /** Idempotent. Wired once at bootstrap via provideAppInitializer. */
    start(): void {
        if (this.started) return;
        this.started = true;
        window.addEventListener('storage', this.onStorage);
        this.destroyRef.onDestroy(() => {
            window.removeEventListener('storage', this.onStorage);
            if (this.retryHandle) clearTimeout(this.retryHandle);
        });
    }

    private readonly onStorage = (ev: StorageEvent): void => {
        // key === null means localStorage.clear() — the token is gone with everything else.
        if (ev.key !== null && ev.key !== LocalStorageKey.AuthToken) return;
        this.reconcile();
    };

    /**
     * Compare what storage says the session is with what this tab is showing; reload if they
     * disagree. Reads storage live rather than trusting the event payload, so a retry after
     * the brake sees the CURRENT state, not a stale one.
     */
    reconcile(): void {
        const stored = storedSession(this.auth.getToken());
        if (stored === 'transitional') return; // another tab is mid-login; wait for the role
        const shown = this.shownSession();
        // A tab that is itself mid-login (Phase-1) never "matches" a settled store: if the
        // session was logged out or completed elsewhere, this tab must catch up too.
        const same = shown !== 'transitional'
            && sameIdentity(stored === 'anonymous' ? null : stored, shown === 'anonymous' ? null : shown);
        if (same) return;

        if (selfHealReload.request()) return;
        // Braked (a self-heal reload happened seconds ago). This trigger cannot loop, but the
        // brake is global to the tab, so wait it out and look again rather than stay a zombie.
        if (this.retryHandle) clearTimeout(this.retryHandle);
        this.retryHandle = setTimeout(() => {
            this.retryHandle = null;
            this.reconcile();
        }, SELF_HEAL_BRAKE_MS);
    }

    /** What this tab is rendering, classified the same way as storage. */
    private shownSession(): StoredSession {
        const u = this.auth.getCurrentUser();
        if (!u?.username) return 'anonymous';
        if (!u.regId) return 'transitional';
        return { username: u.username, regId: u.regId, jobPath: u.jobPath ?? null };
    }
}
