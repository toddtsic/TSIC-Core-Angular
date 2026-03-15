/**
 * Shared credit card sanitization utilities.
 *
 * Pure functions — no Angular DI needed. Import directly where required.
 * Used by both player and team payment/insurance flows.
 */

/** Sanitize credit card expiry to MMYY format (digits only, 4 chars). */
export function sanitizeExpiry(raw?: string): string | undefined {
    const digits = String(raw || '').replaceAll(/\D+/g, '').slice(0, 4);
    if (digits.length === 3) return '0' + digits; // MYY → 0MYY
    return digits.length === 4 ? digits : undefined;
}

/** Strip non-digit characters from phone number, limit to 15 digits. */
export function sanitizePhone(raw?: string): string {
    return String(raw || '').replaceAll(/\D+/g, '').slice(0, 15);
}
