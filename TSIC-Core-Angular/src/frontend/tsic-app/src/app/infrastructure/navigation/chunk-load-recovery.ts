import { NavigationError } from '@angular/router';

/**
 * Deploy-race recovery for lazy route chunks.
 *
 * Every route in app.routes.ts is `loadComponent: () => import(...)` — a separately
 * content-hashed JS chunk fetched on first navigation. Each publish rewrites those hashes and
 * deletes the old files. A tab whose in-memory bundle predates the current deploy still holds
 * the OLD hash map, so a WARM menu navigation (which does not re-fetch index.html) into a
 * not-yet-loaded route does `import('view-schedule-<OLDHASH>.js')` — a file the deploy removed
 * → 404. That rejects the navigation with a chunk-load error.
 *
 * The failure is otherwise silent: a lazy loadComponent rejection surfaces as a router
 * NavigationError (it does NOT reach a global ErrorHandler), and the fetch is the browser's
 * native dynamic import — not HttpClient — so the auth interceptor's error toast never sees it.
 *
 * index.html is served no-cache (angular web.config), so a full reload always pulls fresh hashes
 * and the route then loads. We reload to the URL the user was trying to reach: a public/anonymous
 * viewer (e.g. /schedule) lands right back on it; an authenticated user is cold-started (the
 * intentional "never resume" logout in auth.guard) and lands on job home to re-login — the exact
 * manual "Ctrl+Shift+R then re-navigate" recovery, automated and firing only when a chunk 404s.
 *
 * Wired via provideRouter(withNavigationErrorHandler(...)) in app.config.ts.
 */

const LOOP_GUARD_KEY = 'chunk-reload-at';
// If we already force-reloaded within this window and STILL hit a chunk error, a second reload
// won't help (fresh index.html would already carry correct hashes) — so we bail to avoid a
// reload loop and let the navigation fail as it does today.
const LOOP_GUARD_MS = 15_000;

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

  let lastReloadAt = 0;
  try {
    lastReloadAt = Number(sessionStorage.getItem(LOOP_GUARD_KEY)) || 0;
  } catch {
    /* sessionStorage unavailable (private mode / disabled) — proceed without the guard */
  }

  // Already reloaded very recently and still failing — stop, to avoid a loop.
  if (Date.now() - lastReloadAt < LOOP_GUARD_MS) return;

  try {
    sessionStorage.setItem(LOOP_GUARD_KEY, String(Date.now()));
  } catch {
    /* ignore */
  }

  // Reload to the route the user was navigating to; fall back to the current location.
  const target = navError.url || globalThis.location.pathname + globalThis.location.search;
  globalThis.location.assign(target);
}
