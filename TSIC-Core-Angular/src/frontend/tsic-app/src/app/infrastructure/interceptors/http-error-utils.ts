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
            const entries = Object.entries(errors as Record<string, unknown>);

            // ⛔ A DESERIALIZATION failure is NOT a validation failure, and its messages are
            // internal diagnostics — they name a .NET type, a JSON path, a line number and a
            // byte offset. ASP.NET reports them through the SAME `errors` dictionary as
            // FluentValidation, so without this check they were flattened and toasted verbatim
            // at a director: "The JSON value could not be converted to
            // System.Nullable`1[System.DateTime] … BytePositionInLine: 1073" (AR-088).
            //
            // The tell is the KEY: model binding writes the JSON path (`$.someField`, or `$`)
            // for a body it could not parse, where a validation error is keyed by property name.
            // One such key means the body never deserialized, so NOTHING in the dictionary is a
            // rule the user broke — fall through to the caller's own wording rather than
            // salvaging fragments. The full response is still in the network tab for debugging.
            const isDeserializationFailure = entries.some(([key]) => key.startsWith('$'));
            if (!isDeserializationFailure) {
                const flat = entries
                    .flatMap(([, v]) => (Array.isArray(v) ? v : [v]))
                    .filter((v): v is string => typeof v === 'string' && v.length > 0);
                if (flat.length) return flat.join(' ');
            } else {
                return fallback;
            }
        }

        // Generic last — better than nothing, worse than either of the above.
        const title = body['title'];
        if (typeof title === 'string' && title) return title;
    }

    return fallback;
}
