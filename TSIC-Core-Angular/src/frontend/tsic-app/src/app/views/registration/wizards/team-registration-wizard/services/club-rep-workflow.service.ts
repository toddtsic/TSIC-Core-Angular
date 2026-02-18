import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, throwError, of } from 'rxjs';
import { switchMap, map, catchError } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClubService } from '@infrastructure/services/club.service';
import { TeamRegistrationService } from './team-registration.service';
import type { LoginRequest, ClubRepClubDto, CheckExistingRegistrationsResponse, ClubRepRegistrationRequest } from '@core/api';

/**
 * Result of club rep login workflow
 */
export interface ClubRepLoginResult {
    clubs: ClubRepClubDto[];
    hasConflict: boolean;
    conflictDetails: CheckExistingRegistrationsResponse | null;
}

/**
 * Club Rep Workflow Service
 * 
 * Orchestrates multi-step workflows for club representative operations:
 * - Login → Fetch clubs → Check conflicts (conditional)
 * - Registration → Auto-login → Fetch clubs
 * 
 * Encapsulates business logic and RxJS chains to keep components simple.
 */
@Injectable({
    providedIn: 'root'
})
export class ClubRepWorkflowService {
    private readonly authService = inject(AuthService);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly router = inject(Router);

    /**
     * Execute complete login workflow:
     * 1. Authenticate user
     * 2. Check TOS requirement
     * 3. Fetch user's clubs
     * 4. Optionally check for registration conflicts
     * 
     * @param credentials - Username and password
     * @param jobPath - Current job path for conflict checking
     * @param isRegistrationOpen - Whether to enforce one-rep-per-event rule
     * @returns Observable with clubs and conflict information
     */
    loginAndPrepareClubs(
        credentials: LoginRequest,
        jobPath: string | null,
        isRegistrationOpen: boolean
    ): Observable<ClubRepLoginResult> {
        return this.authService.login(credentials).pipe(
            switchMap(authResponse => {
                // Check if TOS signature required - will navigate away if needed
                if (this.authService.checkAndNavigateToTosIfRequired(authResponse, this.router, this.router.url)) {
                    return throwError(() => ({ type: 'TOS_REQUIRED', message: 'Terms of Service signature required' }));
                }
                // Login successful, proceed to fetch clubs
                return this.teamRegService.getMyClubs();
            }),
            switchMap(clubs => {
                if (clubs.length === 0) {
                    return throwError(() => ({ type: 'NOT_A_CLUB_REP', message: 'You are not registered as a club representative.' }));
                }

                // Determine if we need conflict checking
                const needsConflictCheck = clubs.length === 1 && isRegistrationOpen && jobPath !== null;

                if (needsConflictCheck) {
                    // Single club + registration open → check for conflicts
                    return this.teamRegService.checkExistingRegistrations(jobPath, clubs[0].clubName).pipe(
                        map(conflictCheck => ({
                            clubs,
                            hasConflict: conflictCheck.hasConflict ?? false,
                            conflictDetails: conflictCheck
                        })),
                        catchError((err: unknown) => {
                            console.error('Conflict check failed, proceeding anyway:', err);
                            // On conflict check failure, proceed without conflict data
                            return of({
                                clubs,
                                hasConflict: false,
                                conflictDetails: null
                            });
                        })
                    );
                }

                // Multi-club or registration closed → skip conflict check
                return of({
                    clubs,
                    hasConflict: false,
                    conflictDetails: null
                });
            }),
            catchError((err: unknown) => {
                // Re-throw workflow errors (typed shapes from throwError above)
                const e = err as { type?: string; error?: string | { error?: string; message?: string } };
                if (e?.type === 'TOS_REQUIRED' || e?.type === 'NOT_A_CLUB_REP') {
                    return throwError(() => err);
                }

                // Generic error handling
                let message = 'Login failed. Please check your username and password.';
                if (e?.error) {
                    if (typeof e.error === 'string') {
                        message = e.error;
                    } else if (e.error.error) {
                        message = e.error.error;
                    } else if (e.error.message) {
                        message = e.error.message;
                    }
                }
                return throwError(() => ({ type: 'LOGIN_FAILED', message }));
            })
        );
    }

    /**
     * Execute complete registration workflow:
     * 1. Register new club rep account
     * 2. Attempt auto-login with new credentials
     * 3. Fetch clubs on successful auto-login
     * 
     * @param request - Registration request with club and user details
     * @returns Observable with registration result and clubs (if auto-login succeeds)
     */
    registerAndAutoLogin(
        request: ClubRepRegistrationRequest
    ): Observable<{ success: boolean; clubs?: ClubRepClubDto[]; autoLoginFailed?: boolean }> {
        return this.clubService.registerClub(request).pipe(
            switchMap(registrationResponse => {
                if (!registrationResponse.success) {
                    return throwError(() => ({
                        type: 'REGISTRATION_FAILED',
                        message: registrationResponse.message || 'Registration failed',
                        similarClubs: registrationResponse.similarClubs
                    }));
                }

                // Registration succeeded, attempt auto-login
                return this.authService.login({
                    username: request.username,
                    password: request.password
                }).pipe(
                    switchMap(authResponse => {
                        // Check TOS requirement
                        if (this.authService.checkAndNavigateToTosIfRequired(authResponse, this.router, this.router.url)) {
                            return of({ success: true, clubs: undefined, autoLoginFailed: false });
                        }
                        // Fetch clubs after successful auto-login
                        return this.teamRegService.getMyClubs().pipe(
                            map(clubs => ({ success: true, clubs, autoLoginFailed: false }))
                        );
                    }),
                    catchError((loginErr: unknown) => {
                        // Auto-login failed, but registration succeeded
                        console.error('Auto-login failed after registration:', loginErr);
                        return of({ success: true, clubs: undefined, autoLoginFailed: true });
                    })
                );
            }),
            catchError((err: unknown) => {
                // Registration failed or duplicate club detected
                return throwError(() => err);
            })
        );
    }
}
