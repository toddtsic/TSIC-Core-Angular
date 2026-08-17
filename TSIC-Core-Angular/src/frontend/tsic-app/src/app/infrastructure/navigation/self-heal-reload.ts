/**
 * The ONE way the app reloads itself.
 *
 * Both self-heal paths — a deploy detected by AppVersionService, a lazy chunk the deploy
 * deleted (chunk-load-recovery) — come through here, because HOW the reload is done decides
 * whether the user stays logged in:
 *
 *   history.replaceState(target) + location.reload()
 *
 * `location.reload()` is what the browser reports as a "reload" (see page-load-kind.ts), and
 * auth.guard keeps the session on a reload. `location.assign(url)` reports as "arrived at" and
 * would log the user out — that was the old per-menu-click login bug. Setting the URL first
 * with replaceState means the reload lands on the screen the user was heading to, not the
 * one they left. Verified in Chrome; spec-mandated for Firefox/Safari.
 *
 * Loop brake: a deploy that is mid-copy can leave index.html and version.json (or a chunk)
 * briefly disagreeing. A second reload within BRAKE_MS won't help, so it is refused; the next
 * user action after the window re-checks. sessionStorage is per-tab on purpose — one tab's
 * reload must never suppress another tab's. Auth does NOT read this stamp; it is purely a brake.
 */

const BRAKE_KEY = 'self-heal-reload-at';
const BRAKE_MS = 15_000;

function lastReloadAt(): number {
    try {
        return Number(sessionStorage.getItem(BRAKE_KEY)) || 0;
    } catch {
        return 0; // sessionStorage unavailable (private mode / disabled) — no brake
    }
}

export const selfHealReload = {
    /** True while inside the brake window after a self-heal reload. */
    recentlyReloaded(): boolean {
        return Date.now() - lastReloadAt() < BRAKE_MS;
    },

    /**
     * Reload the page onto `target` (default: current URL). Returns false — and does nothing —
     * when braked. Callers treat false as "leave it; a later action will retry".
     */
    request(target?: string): boolean {
        if (selfHealReload.recentlyReloaded()) return false;
        try {
            sessionStorage.setItem(BRAKE_KEY, String(Date.now()));
        } catch {
            /* ignore */
        }
        selfHealReload.perform(target);
        return true;
    },

    /** The actual browser call, separated so tests can observe it without leaving jsdom. */
    perform(target?: string): void {
        if (target) history.replaceState(history.state, '', target);
        globalThis.location.reload();
    },
};
