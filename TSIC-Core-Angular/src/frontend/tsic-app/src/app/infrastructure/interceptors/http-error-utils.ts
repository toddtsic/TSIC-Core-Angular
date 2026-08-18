/**
 * Extract a user-facing message from an HTTP error response.
 *
 * Precedence: ProblemDetails `detail` / our own `Message` (specific) → FluentValidation
 * `errors` (the field rule that actually failed) → `title` (generic) → the fallback.
 */
export function extractHttpErrorMessage(
    error: unknown,
    fallback = 'An unexpected error occurred.'
): string {
    if (error == null || typeof error !== 'object') return fallback;

    const err = error as Record<string, unknown>;
    const body = (err['error'] ?? err) as Record<string, unknown> | undefined;

    if (body && typeof body === 'object') {
        // ProblemDetails `detail` and our own `Message` are specific; take them first.
        const direct = body['detail'] ?? body['message'] ?? body['Message'];
        if (typeof direct === 'string' && direct) return direct;

        // FluentValidation 400: the reason lives in `errors`, keyed by field. Without this the
        // caller falls through to `title` — always the generic "One or more validation errors
        // occurred." — and the actual rule message never reaches the user.
        const errors = body['errors'];
        if (errors && typeof errors === 'object') {
            const flat = Object.values(errors as Record<string, unknown>)
                .flatMap(v => (Array.isArray(v) ? v : [v]))
                .filter((v): v is string => typeof v === 'string' && v.length > 0);
            if (flat.length) return flat.join(' ');
        }

        // Generic last — better than nothing, worse than either of the above.
        const title = body['title'];
        if (typeof title === 'string' && title) return title;
    }

    return fallback;
}
