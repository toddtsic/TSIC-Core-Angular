import { environment } from '@environments/environment';

/**
 * A Club Rep's schedule-preview invite token, held for this tab.
 *
 * The emailed link carries it in the URL fragment (`/{jobPath}/schedule#preview=<token>`), which the browser
 * never sends to a server. The schedule route's guard captures it here before sending the rep through login;
 * the interceptor presents it as a request header on the schedule-derived API calls, where the server checks
 * it on every request (invited user, Club Rep role, this event, unexpired, preview still on, rep still active).
 * sessionStorage, so it survives the login round-trip and dies with the tab.
 */
const STORAGE_KEY = 'tsic.schedulePreviewToken';

/** Must match ScheduleVisibilityExtensions.SchedulePreviewHeader on the server. */
export const SCHEDULE_PREVIEW_HEADER = 'X-Schedule-Preview';

/** The API surfaces that apply the schedule visibility rule. */
const SCHEDULE_API_PREFIXES = ['/view-schedule', '/job-filter-tree', '/events/'].map(p => `${environment.apiUrl}${p}`);

export function storeSchedulePreviewToken(token: string): void {
    try { sessionStorage.setItem(STORAGE_KEY, token); } catch { /* storage blocked: the preview just won't open */ }
}

export function readSchedulePreviewToken(): string | null {
    try { return sessionStorage.getItem(STORAGE_KEY); } catch { return null; }
}

export function isScheduleApiUrl(url: string): boolean {
    return SCHEDULE_API_PREFIXES.some(p => url.startsWith(p));
}
