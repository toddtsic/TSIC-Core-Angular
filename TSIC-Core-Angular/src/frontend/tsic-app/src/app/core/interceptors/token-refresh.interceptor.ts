import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { ToastService } from '../../shared/toast.service';
import { catchError, switchMap, throwError } from 'rxjs';

/**
 * HTTP Interceptor that handles 401 and 403 errors
 * - 401: Attempts to refresh the access token
 * - 403: Shows toast notification for authorization failures
 */
export const tokenRefreshInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);
    const toastService = inject(ToastService);

    return next(req).pipe(
        catchError((error: HttpErrorResponse) => {
            // Handle 403 Forbidden errors
            if (error.status === 403) {
                const message = error.error?.message ||
                    error.error?.title ||
                    'You do not have permission to access this resource. This may occur if you are trying to access content from a different job path.';
                toastService.show(message, 'danger', 5000);
                return throwError(() => error);
            }

            // Only handle 401 Unauthorized errors
            if (error.status !== 401) {
                return throwError(() => error);
            }

            // Don't try to refresh if the request was to login, refresh, or revoke endpoints
            const isAuthEndpoint = req.url.includes('/auth/login') ||
                req.url.includes('/auth/refresh') ||
                req.url.includes('/auth/revoke');

            if (isAuthEndpoint) {
                return throwError(() => error);
            }

            // Attempt to refresh the token only if we actually possess a refresh token
            const hasRefresh = !!authService.getRefreshToken();
            if (!hasRefresh) {
                // Propagate original 401; upstream guards can redirect. Avoid noisy 'No refresh token' error.
                return throwError(() => error);
            }
            // Attempt to refresh the token
            return authService.refreshAccessToken().pipe(
                switchMap(() => {
                    // Retry the original request with the new token
                    const newToken = authService.getToken();
                    if (newToken) {
                        const clonedRequest = req.clone({
                            setHeaders: {
                                Authorization: `Bearer ${newToken}`
                            }
                        });
                        return next(clonedRequest);
                    }
                    return throwError(() => error);
                }),
                catchError((refreshError) => {
                    // If refresh fails, let the error propagate (will trigger logout in auth service)
                    return throwError(() => refreshError);
                })
            );
        })
    );
};
