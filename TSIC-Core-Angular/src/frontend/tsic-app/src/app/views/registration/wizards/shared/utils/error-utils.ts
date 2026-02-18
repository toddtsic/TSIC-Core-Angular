import { HttpErrorResponse } from '@angular/common/http';

/**
 * Extract a human-readable message from an unknown error.
 * Handles HttpErrorResponse, Error, string, and unknown objects.
 */
export function formatHttpError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
        const apiMsg = err.error?.message;
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
