import { NavigationError } from '@angular/router';
import { selfHealReload } from './self-heal-reload';

/**
 * Deploy-race recovery for lazy route chunks.
 *
 * Every route in app.routes.ts is `loadComponent: () => import(...)` — a separately
 * content-hashed JS chunk fetched on first navigation. Each publish rewrites those hashes and
 * deletes the old files. A tab whose in-memory bundle predates the current deploy still holds
 * the OLD hash map, so a WARM menu navigation (which does not re-fetch index.html) into a
 * not-yet-loaded route does `import('view-schedule-<OLDHASH>.js')` — a file the deploy removed.
 * IIS rewrites the miss to index.html (200, text/html), the dynamic import rejects, and the
 * navigation fails with a chunk-load error.
 *
 * The failure is otherwise silent: a lazy loadComponent rejection surfaces as a router
 * NavigationError (it does NOT reach a global ErrorHandler), and the fetch is the browser's
 * native dynamic import — not HttpClient — so the auth interceptor's error toast never sees it.
 *
 * Recovery: reload onto the URL the user was trying to reach (see self-heal-reload.ts for why
 * it is a reload, and why that keeps the session). index.html is served no-store, so the reload
 * pulls fresh hashes and the route then loads.
 *
 * AppVersionService normally catches a deploy BEFORE this fires (it checks version.json on
 * every NavigationStart); this remains as the backstop for a click that lands inside the
 * deploy's copy window.
 *
 * Wired via provideRouter(withNavigationErrorHandler(...)) in app.config.ts.
 */

/** True for a lazy-chunk fetch failure across bundlers/browsers. */
export function isChunkLoadError(error: unknown): boolean {
    const err = error as { name?: string; message?: string } | null;
    if (!err) return false;
    if (err.name === 'ChunkLoadError') return true;
    const msg = err.message ?? '';
    return (
        /Loading chunk [^\s]+ failed/i.test(msg) ||
        /Failed to fetch dynamically imported module/i.test(msg) ||
        /error loading dynamically imported module/i.test(msg) ||
        /Importing a module script failed/i.test(msg) // Safari wording
    );
}

/**
 * Router navigation-error handler. On a chunk-load failure, one-shot reload to the attempted URL.
 * Any non-chunk navigation error is left alone (returns void → default handling unchanged).
 */
export function chunkLoadRecoveryHandler(navError: NavigationError): void {
    if (!isChunkLoadError(navError.error)) return;
    const target = navError.url || globalThis.location.pathname + globalThis.location.search;
    selfHealReload.request(target);
}
