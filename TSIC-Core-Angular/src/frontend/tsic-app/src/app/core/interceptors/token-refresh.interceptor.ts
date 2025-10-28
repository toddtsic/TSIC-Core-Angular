import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { catchError, switchMap, throwError } from 'rxjs';

/**
 * HTTP Interceptor that handles 401 errors by attempting to refresh the access token
 */
export const tokenRefreshInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);

    return next(req).pipe(
        catchError((error: HttpErrorResponse) => {
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
