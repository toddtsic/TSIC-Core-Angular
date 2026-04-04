/**
 * Extract a user-facing message from an HTTP error response.
 *
 * Checks ProblemDetails fields (detail, title) first, then generic message
 * fields, falling back to the provided default.
 */
export function extractHttpErrorMessage(
    error: unknown,
    fallback = 'An unexpected error occurred.'
): string {
    if (error == null || typeof error !== 'object') return fallback;

    const err = error as Record<string, unknown>;
    const body = (err['error'] ?? err) as Record<string, unknown> | undefined;

    if (body && typeof body === 'object') {
        // ProblemDetails: detail → title → message (case-insensitive .NET)
        const msg = body['detail'] ?? body['title'] ?? body['message'] ?? body['Message'];
        if (typeof msg === 'string' && msg) return msg;
    }

    return fallback;
}
