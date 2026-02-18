import { HttpErrorResponse } from '@angular/common/http';

/**
 * Extract a human-readable message from an unknown error.
 * Handles HttpErrorResponse, Error, string, and unknown objects.
 */
export function formatHttpError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
        // Check both lowercase (.message) and PascalCase (.Message) — .NET APIs may use either
        const apiMsg = err.error?.message ?? err.error?.Message;
        if (typeof apiMsg === 'string' && apiMsg) return apiMsg;
        return err.message || `HTTP ${err.status}`;
    }
    if (err instanceof Error) {
        return err.message;
    }
    if (typeof err === 'string') {
        return err;
    }
    return 'An unexpected error occurred';
}
