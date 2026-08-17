import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';
import { environment } from '@environments/environment';
import { selfHealReload } from '../navigation/self-heal-reload';

/**
 * "After I deploy, my users get the new code."
 *
 * A single-page app never re-fetches index.html on its own; a tab open across a deploy runs
 * the old bundle until something forces a full page load. Screens the tab has already
 * visited are in memory and never touch the server again, so the old chunk-recovery (which
 * only fires when a chunk is MISSING) cannot see the deploy at all — that is the
 * "click the smart bulletin, still get the old bug" case.
 *
 * This service asks. On every URL change (router NavigationStart) and whenever the tab
 * comes back into view, it fetches /version.json — a one-field file the deploy writes next to
 * index.html, served no-store — and compares it to the stamp compiled into this bundle
 * (environment.buildVersion). Different ⇒ a deploy landed ⇒ full reload onto the screen the
 * user was heading to. The reload keeps the session (see self-heal-reload.ts).
 *
 * No timer, no polling. The check rides the user's own actions: any click after a deploy
 * loads new code, once, and the tab is then current until the next deploy. A tab nobody
 * touches is not checked — by design.
 *
 * WHEN the reload happens, precisely:
 *   • stale detected while a navigation is in flight → on that navigation's NavigationEnd
 *     (the user is arriving at a new screen; nothing typed yet, deactivate guards already ran);
 *   • stale detected while the router is idle (fetch outran a fast in-memory navigation, or
 *     the tab just came back into view) → immediately, onto the current URL.
 *   Either way it is one reload per detected deploy.
 *
 * NEVER reloads on "don't know": fetch failure, non-2xx (503 while the app pool is stopped
 * mid-cutover), or a non-JSON body (an old server without version.json hands back index.html
 * through the SPA rewrite). Disabled entirely under `ng serve` (buildVersion === 'dev').
 */
@Injectable({ providedIn: 'root' })
export class AppVersionService {
    private readonly router = inject(Router);
    private readonly destroyRef = inject(DestroyRef);

    private started = false;
    private checking = false;
    /** A newer build is on the server; reload at the next safe point. */
    private stale = false;

    /** Idempotent. Wired once at bootstrap via provideAppInitializer. */
    start(): void {
        if (this.started) return;
        this.started = true;
        if (!this.enabled()) return;

        this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(ev => {
            if (ev instanceof NavigationStart) void this.check();
            else if (ev instanceof NavigationEnd && this.stale) this.reloadNow();
        });

        document.addEventListener('visibilitychange', this.onVisibilityChange);
        this.destroyRef.onDestroy(() =>
            document.removeEventListener('visibilitychange', this.onVisibilityChange));
    }

    /** False under `ng serve` — no version.json exists and the stamp is the literal 'dev'. */
    enabled(): boolean {
        return this.compiledStamp() !== 'dev';
    }

    /**
     * Ask the server which build it is serving. Resolves true when it differs from ours (and
     * arms / performs the reload); false when same or unknown. Coalesces overlapping calls.
     * Never throws.
     */
    async check(): Promise<boolean> {
        if (this.stale) return true;
        if (this.checking) return false;
        this.checking = true;
        try {
            const served = await this.fetchServedStamp();
            if (!served || served === this.compiledStamp()) return false;
            this.stale = true;
            // Router idle → nothing to wait for. Otherwise NavigationEnd (see start) reloads.
            if (!this.router.getCurrentNavigation()) this.reloadNow();
            return true;
        } finally {
            this.checking = false;
        }
    }

    private readonly onVisibilityChange = (): void => {
        if (document.visibilityState === 'visible') void this.check();
    };

    private compiledStamp(): string {
        return environment.buildVersion;
    }

    private reloadNow(): void {
        // Braked (a self-heal reload happened seconds ago and the stamps STILL disagree — the
        // deploy is mid-copy): disarm rather than retry on every NavigationEnd. The next
        // NavigationStart re-checks, by which time the brake has likely expired.
        if (!selfHealReload.request()) this.stale = false;
    }

    private async fetchServedStamp(): Promise<string | null> {
        try {
            const url = new URL('version.json', document.baseURI).toString();
            const res = await fetch(url, { cache: 'no-store', credentials: 'omit' });
            if (!res.ok) return null;
            const type = res.headers.get('content-type') ?? '';
            if (!type.includes('json')) return null;
            const body = (await res.json()) as { buildStamp?: unknown } | null;
            const stamp = body?.buildStamp;
            return typeof stamp === 'string' && stamp.length > 0 ? stamp : null;
        } catch {
            return null;
        }
    }
}
