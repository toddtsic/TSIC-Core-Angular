import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap, map } from 'rxjs/operators';
import {
  LoginRequest,
  LoginResponse,
  RoleSelectionRequest,
  AuthTokenResponse,
  AuthenticatedUser,
  RegistrationRoleDto
} from '../models/auth.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly apiUrl = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';

  private currentUserSubject = new BehaviorSubject<AuthenticatedUser | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
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
          // Store initial token (has username claim only)
          this.setToken(response.accessToken);
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
          // Store full token (has username, jobPath, regId claims)
          this.setToken(response.accessToken);
          this.initializeFromToken();
        })
      );
  }

  /**
   * Logout - clear stored auth data
   */
  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.currentUserSubject.next(null);
  }

  /**
   * Check if user is authenticated (has any token)
   */
  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  /**
   * Check if user has selected a role (token has regId claim)
   */
  hasSelectedRole(): boolean {
    const user = this.currentUserSubject.value;
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
    return this.currentUserSubject.value;
  }

  /**
   * Get job path from token claims
   */
  getJobPath(): string | null {
    const user = this.currentUserSubject.value;
    return user?.jobPath || null;
  }

  private setToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  /**
   * Decode JWT token and extract user info
   */
  private initializeFromToken(): void {
    const token = this.getToken();
    if (!token) {
      this.currentUserSubject.next(null);
      return;
    }

    try {
      const payload = this.decodeToken(token);
      const user: AuthenticatedUser = {
        username: payload.username || payload.sub,
        regId: payload.regId,
        jobPath: payload.jobPath
      };
      this.currentUserSubject.next(user);
    } catch (error) {
      console.error('Failed to decode token:', error);
      this.currentUserSubject.next(null);
    }
  }

  /**
   * Decode JWT token payload
   */
  private decodeToken(token: string): any {
    try {
      const base64Url = token.split('.')[1];
      const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
      const jsonPayload = decodeURIComponent(
        atob(base64)
          .split('')
          .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
          .join('')
      );
      return JSON.parse(jsonPayload);
    } catch (error) {
      throw new Error('Invalid token format');
    }
  }
}
