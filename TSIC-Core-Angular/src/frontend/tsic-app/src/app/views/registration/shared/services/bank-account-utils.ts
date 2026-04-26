/**
 * Shared bank account (eCheck / ACH) sanitization utilities.
 * Pure functions — no Angular DI needed. Used by both player and team payment flows.
 *
 * Field caps mirror Authorize.Net's bankAccountType XSD:
 *   - routing: exactly 9 digits
 *   - account: 4-17 alphanumeric chars
 *   - nameOnAccount: ≤22 chars
 */

/** Strip non-digit characters from routing number, cap at 9 digits. */
export function sanitizeRouting(raw?: string): string {
    return String(raw || '').replaceAll(/\D+/g, '').slice(0, 9);
}

/** Strip non-alphanumeric characters from account number, cap at 17 chars. */
export function sanitizeAccount(raw?: string): string {
    return String(raw || '').replaceAll(/[^a-zA-Z0-9]+/g, '').slice(0, 17);
}

/** Trim and cap name-on-account at 22 chars. */
export function sanitizeNameOnAccount(raw?: string): string {
    return String(raw || '').trim().slice(0, 22);
}
