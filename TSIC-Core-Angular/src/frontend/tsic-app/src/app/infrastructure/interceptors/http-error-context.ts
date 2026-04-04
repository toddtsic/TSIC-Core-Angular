import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * When set to true on a request's HttpContext, the global error interceptor
 * will NOT show a toast for that request. Use this when the calling component
 * already displays its own inline error UI.
 */
export const SKIP_GLOBAL_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Convenience helper — returns an HttpContext with the skip flag set. */
export function skipErrorToast(): HttpContext {
    return new HttpContext().set(SKIP_GLOBAL_ERROR_TOAST, true);
}
