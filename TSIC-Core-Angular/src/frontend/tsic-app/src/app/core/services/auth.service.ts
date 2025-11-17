import { Injectable, inject, signal, computed } from '@angular/core';
import { Roles, RoleName } from '../models/roles.constants';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
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

  // Signals for UI flows
  public readonly loginLoading = signal(false);
  public readonly loginError = signal<string | null>(null);
  public readonly registrations = signal<RegistrationRoleDto[]>([]);
  public readonly registrationsLoading = signal(false);
  public readonly registrationsError = signal<string | null>(null);
  // Internal flag so we don't refetch registrations repeatedly in the same session
  private _registrationsFetched = false;
  public readonly selectLoading = signal(false);
  public readonly selectError = signal<string | null>(null);

  // Computed signals for derived state
  public readonly isSuperuser = computed(() => {
    const user = this.currentUser();
    return !!user?.roles?.includes(Roles.Superuser) || user?.role === Roles.Superuser;
  });

  public readonly isAdmin = computed(() => {
    const user = this.currentUser();
    const roles = user?.roles || (user?.role ? [user.role] : []);
    return roles.includes(Roles.Superuser) || roles.includes(Roles.Director) || roles.includes(Roles.SuperDirector);
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
          this.setToken(response.accessToken!);
          if (response.refreshToken) this.setRefreshToken(response.refreshToken);
          this.initializeFromToken();
        })
      );
  }

  /**
   * Command-style login that updates signals instead of returning an Observable
   */
  loginCommand(credentials: LoginRequest): void {
    this.loginLoading.set(true);
    this.loginError.set(null);
    this.http.post<AuthTokenResponse>(`${this.apiUrl}/login`, credentials).subscribe({
      next: (response) => {
        this.setToken(response.accessToken!);
        if (response.refreshToken) this.setRefreshToken(response.refreshToken);
        this.initializeFromToken();
        this.loginLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loginLoading.set(false);
        const msg = error?.error?.message || error?.message || 'Login failed. Please check your credentials.';
        this.loginError.set(msg);
      }
    });
  }

  /**
   * Phase 2: Get available registrations for the authenticated user
   * Requires initial auth token with username claim
   */
  getAvailableRegistrations(): Observable<RegistrationRoleDto[]> {
    return this.http.get<LoginResponse>(`${this.apiUrl}/registrations`)
      .pipe(map(response => response.registrations ?? []));
  }

  /**
   * Command-style fetch for available registrations using signals
   */
  loadAvailableRegistrations(): void {
    // Guard: only fetch once unless explicitly reset (e.g., on logoutLocal or explicit manual refresh in future)
    if (this._registrationsFetched) return;
    this.registrationsLoading.set(true);
    this.registrationsError.set(null);
    this.http.get<LoginResponse>(`${this.apiUrl}/registrations`).subscribe({
      next: (resp) => {
        this.registrations.set(resp.registrations ?? []);
        this.registrationsLoading.set(false);
        this._registrationsFetched = true;
      },
      error: (error: HttpErrorResponse) => {
        this.registrationsLoading.set(false);
        const msg = error?.error?.message || error?.message || 'Failed to load registrations. Please try again.';
        this.registrationsError.set(msg);
      }
    });
  }

  /**
   * Phase 3: Select a registration and receive full JWT token
   * Returns new token with jobPath and regId claims
   */
  selectRegistration(regId: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/select-registration`, { regId })
      .pipe(
        tap(response => {
          this.setToken(response.accessToken!);
          if (response.refreshToken) this.setRefreshToken(response.refreshToken);
          this.initializeFromToken();
        })
      );
  }

  /**
   * Command-style registration selection via signals
   */
  selectRegistrationCommand(regId: string): void {
    if (!regId) return;
    this.selectLoading.set(true);
    this.selectError.set(null);
    this.http.post<AuthTokenResponse>(`${this.apiUrl}/select-registration`, { regId }).subscribe({
      next: (response) => {
        this.setToken(response.accessToken!);
        if (response.refreshToken) this.setRefreshToken(response.refreshToken);
        this.initializeFromToken();
        this.selectLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.selectLoading.set(false);
        const msg = error?.error?.message || error?.message || 'Role selection failed. Please try again.';
        this.selectError.set(msg);
      }
    });
  }

  /**
   * Logout - revoke refresh token and clear stored auth data
   */
  logout(options?: { redirectTo?: string; queryParams?: Record<string, any> }): void {
    const refreshToken = this.getRefreshToken();

    // Revoke refresh token on server if it exists
    if (refreshToken) {
      this.http.post(`${this.apiUrl}/revoke`, { refreshToken }).subscribe({
        error: (err) => { if (!environment.production) console.error('Error revoking token:', err); }
      });
    }

    // Clear local storage
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    this.currentUser.set(null);
    const redirect = options?.redirectTo || '/tsic/login';
    const q = options?.queryParams || undefined;
    if (q) {
      this.router.navigate([redirect], { queryParams: q });
    } else {
      this.router.navigate([redirect]);
    }
  }

  /**
   * Clear local auth state without navigating or calling the server.
   * Useful for flows that need a clean slate while staying on the same route.
   */
  logoutLocal(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    this.currentUser.set(null);
    // reset one-shot registration fetch so next authenticated flow can reload
    this._registrationsFetched = false;
    this.registrations.set([]);
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
      if (!environment.production) console.error('Failed to check token expiration:', error);
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
          this.setToken(response.accessToken!);
          if (response.refreshToken) {
            this.setRefreshToken(response.refreshToken);
          }
          this.initializeFromToken();
        }),
        catchError((error: HttpErrorResponse) => {
          // If refresh fails, logout user
          this.logout();
          return throwError(() => error);
        })
      );
  }

  /**
   * Decode JWT token and extract user info
   * For expired tokens, still populate user from token payload to avoid logout,
   * but let the interceptor handle refresh on next API call
   */
  private initializeFromToken(): void {
    const token = this.getToken();
    if (!token) {
      this.currentUser.set(null);
      return;
    }

    try {
      const payload = this.decodeToken(token);

      // Extract user info from token payload regardless of expiration
      // Normalize role claim(s) -> roles array.
      // Server presently issues a single role claim name; future enhancement may emit an array.
      const rawRole = payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
      let roles: RoleName[] | undefined = undefined;
      if (rawRole) {
        if (Array.isArray(rawRole)) {
          roles = rawRole as RoleName[];
        } else if (typeof rawRole === 'string') {
          roles = [rawRole as RoleName];
        }
      }
      const user: AuthenticatedUser = {
        username: payload.username || payload.sub,
        regId: payload.regId,
        jobPath: payload.jobPath,
        jobLogo: payload.jobLogo,
        role: roles?.[0] || rawRole,
        roles
      };
      this.currentUser.set(user);

      // Check if token is expired
      if (payload.exp) {
        const expirationDate = new Date(payload.exp * 1000);
        const now = new Date();

        if (expirationDate <= now) {
          if (!environment.production) console.warn('Access token expired. Will refresh on next API call via interceptor.');
          // Don't proactively refresh here - let the token interceptor handle it
          // This avoids race conditions with route guards
        }
      }
    } catch (error) {
      if (!environment.production) console.error('Failed to decode token:', error);
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
      if (!environment.production) console.error('Token decode error:', error);
      throw new Error('Invalid token format');
    }
  }
}
