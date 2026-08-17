/**
 * Decode a JWT's payload WITHOUT verifying it. Client-side only, for reading claims the
 * server already vouched for (identity, jobPath, expiry). Returns null for anything that
 * isn't a well-formed token — never throws.
 */
export function decodeJwtPayload(token: string | null | undefined): Record<string, unknown> | null {
    if (!token) return null;
    const parts = token.split('.');
    if (parts.length < 2) return null;
    try {
        const b64 = parts[1].replaceAll('-', '+').replaceAll('_', '/');
        const padded = b64 + '='.repeat((4 - (b64.length % 4)) % 4);
        const bytes = Uint8Array.from(atob(padded), c => c.charCodeAt(0));
        const parsed: unknown = JSON.parse(new TextDecoder().decode(bytes));
        return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}
