import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap, map } from 'rxjs/operators';
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

  private readonly currentUserSubject = new BehaviorSubject<AuthenticatedUser | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

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
   * Logout - clear stored auth data and redirect to login
   */
  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.currentUserSubject.next(null);
    this.router.navigate(['/tsic/login']);
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
      console.log('Decoded token payload:', payload);
      const user: AuthenticatedUser = {
        username: payload.username || payload.sub,
        regId: payload.regId,
        jobPath: payload.jobPath
      };
      console.log('Extracted user:', user);
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
