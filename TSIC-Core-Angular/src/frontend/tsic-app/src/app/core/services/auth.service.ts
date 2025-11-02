import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, throwError } from 'rxjs';
import { tap, map, catchError } from 'rxjs/operators';
import {
  LoginRequest,
  LoginResponse,
  AuthTokenResponse,
  AuthenticatedUser,
  RegistrationRoleDto
} from '../models/auth.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly REFRESH_TOKEN_KEY = 'refresh_token';

  // Signal for reactive state management
  public readonly currentUser = signal<AuthenticatedUser | null>(null);

  // Computed signals for derived state
  public readonly isSuperuser = computed(() => {
    const user = this.currentUser();
    return user?.role?.toLowerCase() === 'superuser';
  });

  public readonly isAdmin = computed(() => {
    const user = this.currentUser();
    const role = user?.role?.toLowerCase();
    return role === 'superuser' || role === 'admin';
  });

  constructor() {
    // Initialize current user from token on service creation
    this.initializeFromToken();
  }

  /**
   * Phase 1: Login with username and password
   * Returns JWT with minimal claims (username only)
   */
  login(credentials: LoginRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(
        tap(response => {
          // Store both access token and refresh token
          this.setToken(response.accessToken);
          if (response.refreshToken) {
            this.setRefreshToken(response.refreshToken);
          }
          this.initializeFromToken();
        })
      );
  }

  /**
   * Phase 2: Get available registrations for the authenticated user
   * Requires initial auth token with username claim
   */
  getAvailableRegistrations(): Observable<RegistrationRoleDto[]> {
    return this.http.get<LoginResponse>(`${this.apiUrl}/registrations`)
      .pipe(
        map(response => response.registrations)
      );
  }

  /**
   * Phase 3: Select a registration and receive full JWT token
   * Returns new token with jobPath and regId claims
   */
  selectRegistration(regId: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/select-registration`, { regId })
      .pipe(
        tap(response => {
          // Store both tokens
          this.setToken(response.accessToken);
          if (response.refreshToken) {
            this.setRefreshToken(response.refreshToken);
          }
          this.initializeFromToken();
        })
      );
  }

  /**
   * Logout - revoke refresh token and clear stored auth data
   */
  logout(): void {
    const refreshToken = this.getRefreshToken();

    // Revoke refresh token on server if it exists
    if (refreshToken) {
      this.http.post(`${this.apiUrl}/revoke`, { refreshToken })
        .subscribe({
          error: (err) => console.error('Error revoking token:', err)
        });
    }

    // Clear local storage
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    this.currentUser.set(null);
    this.router.navigate(['/tsic/login']);
  }

  /**
 * Check if user has valid token
 * Returns true if token exists and is not expired
 */
  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) {
      return false;
    }

    try {
      const payload = this.decodeToken(token);
      if (payload.exp) {
        const expirationDate = new Date(payload.exp * 1000);
        const now = new Date();
        // Return false if token is expired
        // The refresh will happen in initializeFromToken, but guards need to know current state
        return expirationDate > now;
      }
      // If no exp claim, assume valid (shouldn't happen with JWT)
      return true;
    } catch (error) {
      console.error('Failed to check token expiration:', error);
      return false;
    }
  }

  /**
   * Check if user has selected a role (token has regId claim)
   */
  hasSelectedRole(): boolean {
    const user = this.currentUser();
    return !!(user?.regId);
  }

  /**
   * Get stored JWT token
   */
  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  /**
   * Get current authenticated user (decoded from token)
   */
  getCurrentUser(): AuthenticatedUser | null {
    return this.currentUser();
  }

  /**
   * Get job path from token claims
   */
  getJobPath(): string | null {
    const user = this.currentUser();
    return user?.jobPath || null;
  }

  private setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  private setRefreshToken(token: string): void {
    localStorage.setItem(this.REFRESH_TOKEN_KEY, token);
  }

  /**
   * Get stored refresh token
   */
  getRefreshToken(): string | null {
    return localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  /**
   * Refresh the access token using the refresh token
   */
  refreshAccessToken(): Observable<AuthTokenResponse> {
    const refreshToken = this.getRefreshToken();

    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }

    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/refresh`, { refreshToken })
      .pipe(
        tap(response => {
          this.setToken(response.accessToken);
          if (response.refreshToken) {
            this.setRefreshToken(response.refreshToken);
          }
          this.initializeFromToken();
        }),
        catchError(error => {
          // If refresh fails, logout user
          this.logout();
          return throwError(() => error);
        })
      );
  }

  /**
   * Decode JWT token and extract user info
   */
  private initializeFromToken(): void {
    const token = this.getToken();
    if (!token) {
      this.currentUser.set(null);
      return;
    }

    try {
      const payload = this.decodeToken(token);

      // Check if token is expired
      if (payload.exp) {
        const expirationDate = new Date(payload.exp * 1000);
        const now = new Date();

        if (expirationDate <= now) {
          console.warn('Access token expired. Guards will handle refresh on next navigation.');
          // Don't set user to null yet - let the guard attempt refresh
          // This prevents race conditions during app initialization
          return;
        }
      }

      const user: AuthenticatedUser = {
        username: payload.username || payload.sub,
        regId: payload.regId,
        jobPath: payload.jobPath,
        jobLogo: payload.jobLogo,
        role: payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
      };
      this.currentUser.set(user);
    } catch (error) {
      console.error('Failed to decode token:', error);
      this.currentUser.set(null);
    }
  }

  /**
   * Decode JWT token payload
   */
  private decodeToken(token: string): any {
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
      return JSON.parse(jsonPayload);
    } catch (error) {
      console.error('Token decode error:', error);
      throw new Error('Invalid token format');
    }
  }
}
