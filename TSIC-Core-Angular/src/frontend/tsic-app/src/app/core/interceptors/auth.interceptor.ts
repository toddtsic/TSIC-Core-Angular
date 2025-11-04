import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { environment } from '../../../environments/environment';
import { switchMap, catchError } from 'rxjs';

/**
 * HTTP Interceptor that adds JWT token to all outgoing requests
 * Proactively refreshes expired tokens before making requests
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);
    const token = authService.getToken();

    // Skip auth header only for endpoints that must not include it
    // - login: authenticates with credentials
    // - refresh: uses refresh token body
    // All other endpoints (including registrations/select-registration) should include Authorization
    const isAuthEndpoint = req.url.includes('/auth/login') ||
        req.url.includes('/auth/refresh');

    if (isAuthEndpoint) {
        // Don't add any auth header to auth endpoints (they handle their own auth via request body)
        return next(req);
    }

    if (!token) {
        return next(req);
    }

    // Check if token is expired
    const isExpired = isTokenExpired(token);

    if (isExpired) {
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
                return next(req);
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
                return next(req);
            })
        );
    }

    // Token is valid, add it to request
    req = req.clone({
        setHeaders: {
            Authorization: `Bearer ${token}`
        }
    });

    return next(req);
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
