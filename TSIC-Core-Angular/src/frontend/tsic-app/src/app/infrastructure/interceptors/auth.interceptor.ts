import { HttpInterceptorFn, HttpErrorResponse, HttpRequest, HttpEvent } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';
import { switchMap, catchError, throwError, Observable } from 'rxjs';

/**
 * HTTP Interceptor that:
 * - Adds JWT token to all outgoing requests
 * - Proactively refreshes expired tokens before making requests
 * - Handles 401 errors by attempting token refresh
 * - Handles 403 errors with toast notifications
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);
    const toastService = inject(ToastService);
    const token = authService.getToken();
    const refreshToken = authService.getRefreshToken();

    /**
     * Helper function to execute request with error handling for 401 and 403
     */
    const handleRequest = (request: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
        return next(request).pipe(
            catchError((error: HttpErrorResponse) => {
                // Handle 403 Forbidden errors
                if (error.status === 403) {
                    const errorType = error.error?.type;

                    // Check for specific JobPathMismatch error
                    if (errorType === 'JobPathMismatch') {
                        const message = error.error?.detail ||
                            `Access denied: You're logged into '${error.error?.extensions?.tokenJobPath}' but tried to access '${error.error?.extensions?.routeJobPath}'.`;
                        toastService.show(message, 'danger', 7000);
                    } else {
                        // Generic 403 error - try detail then title
                        const message = error.error?.detail ||
                            error.error?.title ||
                            'You do not have permission to access this resource.';
                        toastService.show(message, 'danger', 5000);
                    }
                    return throwError(() => error);
                }

                // Handle 401 Unauthorized errors
                if (error.status === 401) {
                    // Don't try to refresh if the request was to auth endpoints
                    const isAuthEndpoint = request.url.includes('/auth/login') ||
                        request.url.includes('/auth/refresh') ||
                        request.url.includes('/auth/revoke');

                    if (isAuthEndpoint) {
                        return throwError(() => error);
                    }

                    // Attempt to refresh the token only if we have a refresh token
                    const hasRefresh = !!authService.getRefreshToken();
                    if (!hasRefresh) {
                        return throwError(() => error);
                    }

                    // Attempt to refresh the token
                    return authService.refreshAccessToken().pipe(
                        switchMap(() => {
                            // Retry the original request with the new token
                            const newToken = authService.getToken();
                            if (newToken) {
                                const clonedRequest = request.clone({
                                    setHeaders: {
                                        Authorization: `Bearer ${newToken}`
                                    }
                                });
                                // Use next() directly for retry to avoid infinite 401 loops
                                // But still handle 403 errors on the retry
                                return next(clonedRequest).pipe(
                                    catchError((retryError: HttpErrorResponse) => {
                                        if (retryError.status === 403) {
                                            const errorType = retryError.error?.type;

                                            if (errorType === 'JobPathMismatch') {
                                                const msg = retryError.error?.detail ||
                                                    `Access denied: You're logged into '${retryError.error?.extensions?.tokenJobPath}' but tried to access '${retryError.error?.extensions?.routeJobPath}'.`;
                                                toastService.show(msg, 'danger', 7000);
                                            } else {
                                                const msg = retryError.error?.detail ||
                                                    retryError.error?.title ||
                                                    'You do not have permission to access this resource.';
                                                toastService.show(msg, 'danger', 5000);
                                            }
                                        }
                                        return throwError(() => retryError);
                                    })
                                );
                            }
                            return throwError(() => error);
                        }),
                        catchError((refreshError) => {
                            return throwError(() => refreshError);
                        })
                    );
                }

                return throwError(() => error);
            })
        );
    };

    // Skip auth header only for endpoints that must not include it
    // - login: authenticates with credentials
    // - refresh: uses refresh token body
    // All other endpoints (including registrations/select-registration) should include Authorization
    const isAuthEndpoint = req.url.includes('/auth/login') ||
        req.url.includes('/auth/refresh');

    if (isAuthEndpoint) {
        // Don't add any auth header to auth endpoints (they handle their own auth via request body)
        return handleRequest(req);
    }

    if (!token) {
        return handleRequest(req);
    }

    // Check if token is expired
    const isExpired = isTokenExpired(token);

    if (isExpired) {
        // If we have no refresh token, do not attempt refresh; proceed and allow 401 handling / guards to redirect.
        if (!refreshToken) {
            if (token) {
                req = req.clone({
                    setHeaders: {
                        Authorization: `Bearer ${token}`
                    }
                });
            }
            return handleRequest(req);
        }
        if (!environment.production) {
            console.log('Token expired, refreshing before request...');
        }
        // Refresh token first, then make the request with new token
        return authService.refreshAccessToken().pipe(
            switchMap(() => {
                const newToken = authService.getToken();
                if (newToken) {
                    req = req.clone({
                        setHeaders: {
                            Authorization: `Bearer ${newToken}`
                        }
                    });
                }
                return handleRequest(req);
            }),
            catchError((error) => {
                // If refresh fails, proceed with original request
                // (will likely fail with 401, triggering logout)
                if (token) {
                    req = req.clone({
                        setHeaders: {
                            Authorization: `Bearer ${token}`
                        }
                    });
                }
                return handleRequest(req);
            })
        );
    }

    // Token is valid, add it to request
    req = req.clone({
        setHeaders: {
            Authorization: `Bearer ${token}`
        }
    });

    return handleRequest(req);
};

/**
 * Check if a JWT token is expired
 */
function isTokenExpired(token: string): boolean {
    try {
        const base64Url = token.split('.')[1];
        const base64 = base64Url.replaceAll('-', '+').replaceAll('_', '/');
        const jsonPayload = decodeURIComponent(
            atob(base64)
                .split('')
                .map(c => {
                    const code = c.codePointAt(0);
                    return '%' + ('00' + (code ? code.toString(16) : '00')).slice(-2);
                })
                .join('')
        );
        const payload = JSON.parse(jsonPayload);

        if (payload.exp) {
            const expirationDate = new Date(payload.exp * 1000);
            const now = new Date();
            return expirationDate <= now;
        }
        return false;
    } catch (error) {
        if (!environment.production) {
            console.error('Failed to check token expiration:', error);
        }
        return false;
    }
}
