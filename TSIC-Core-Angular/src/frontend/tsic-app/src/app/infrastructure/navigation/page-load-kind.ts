/**
 * How THIS document was opened — the one fact the session rule keys on.
 *
 * The rule (auth.guard): a page that was RELOADED keeps its session; a page that was ARRIVED
 * AT from outside (emailed link, typed URL, bookmark, new tab, back-button from another site)
 * starts clean. An email can only ever produce "arrived at", so an invite link is always judged
 * against whoever logs in FOR it — never against whoever was left in the browser. The app's own
 * reloads (new build detected, missing lazy chunk, user pressing F5) all read as "reloaded" and
 * keep the user where they were, still logged in.
 *
 * The browser reports this itself; nothing is stored. That is the point — the previous design
 * wrote a "trust me, I reloaded myself" stamp to sessionStorage, and every new reason to reload
 * needed the same exemption. Reading the navigation type needs none.
 *
 * Fail-closed: if the browser exposes neither the modern nor the legacy API, answer false
 * ("arrived at") and the guard logs out — today's behavior, never a kept session by accident.
 */
export function wasPageReloaded(): boolean {
    try {
        const entries = performance.getEntriesByType?.('navigation') as
            | PerformanceNavigationTiming[]
            | undefined;
        const entry = entries?.[0];
        if (entry) return entry.type === 'reload';

        // Legacy PerformanceNavigation (older Safari): TYPE_RELOAD === 1.
        const legacy = (performance as { navigation?: { type?: number } }).navigation;
        if (legacy && typeof legacy.type === 'number') return legacy.type === 1;
    } catch {
        /* fall through — fail closed */
    }
    return false;
}
