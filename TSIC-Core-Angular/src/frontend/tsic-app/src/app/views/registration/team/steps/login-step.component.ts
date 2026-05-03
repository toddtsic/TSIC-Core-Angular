import { ChangeDetectionStrategy, Component, inject, OnInit, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClubService } from '@infrastructure/services/club.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { ClubRepRegisterFormComponent } from './club-rep-register-form.component';
import type { ClubRepClubDto, ClubRepProfileDto } from '@core/api';

export interface LoginStepResult {
    availableClubs: ClubRepClubDto[];
    clubName: string | null;
}

type LoginView = 'sign-in' | 'create' | 'account-summary' | 'edit-profile';

/**
 * Team wizard's "Login" tab — single home for everything club-rep account related:
 *   sign-in, create-account (inline), and edit-account (inline).
 * Wizard auto-advances past this step on successful sign-in or create
 * (see team.component.ts advancePastLogin); the summary + edit views are seen
 * only when an authenticated user navigates back via the step indicator.
 */
@Component({
    selector: 'app-trw-login-step',
    standalone: true,
    imports: [FormsModule, LoginComponent, ClubRepRegisterFormComponent],
    styles: [`
      :host { display: block; }

      .account-step {
        max-width: 460px;
        margin: 0 auto;
        padding-top: var(--space-8);
      }
      /* Create/edit views need the full wizard width (matches Waivers/Teams cards) */
      .account-step--wide {
        max-width: 720px;
      }

      .or-divider {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        margin: var(--space-4) 0;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
      }
      .or-divider::before,
      .or-divider::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--border-color);
      }

      .create-cta {
        text-align: center;
      }
      .create-cta p {
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        margin-bottom: var(--space-3);
      }

      .back-link {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        background: none;
        border: none;
        padding: 0 0 var(--space-3);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
      }
      .back-link:hover { text-decoration: underline; }
      .back-link:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }

      /* Card-header treatment for create/edit views — matches Waivers step pattern */
      .create-header h5,
      .edit-header h5 {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-weight: var(--font-weight-bold);
      }
      .create-header h5 i,
      .edit-header h5 i { color: var(--bs-primary); }
      .new-pill {
        display: inline-block;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: white;
        background: var(--bs-primary);
        border-radius: var(--radius-full);
        letter-spacing: 0.04em;
      }

      /* Account summary view */
      .summary-identity {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        margin-bottom: var(--space-4);
      }
      .summary-identity i {
        color: var(--bs-success);
        font-size: 1.5rem;
        flex-shrink: 0;
      }
      .summary-identity .name {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        line-height: 1.2;
      }
      .summary-identity .email {
        display: block;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        margin-top: 2px;
      }
      .summary-actions {
        display: flex;
        gap: var(--space-2);
      }
      .summary-actions .btn { flex: 0 0 auto; }

      .summary-loading {
        text-align: center;
        padding: var(--space-3);
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
      }

      @media (max-width: 575.98px) {
        .account-step { padding-top: var(--space-4); }
        .or-divider { margin: var(--space-2) 0; font-size: var(--font-size-xs); }
        .create-cta p { font-size: var(--font-size-xs); margin-bottom: var(--space-2); }
        .summary-actions { flex-direction: column; }
        .summary-actions .btn { flex: 1; }
      }
    `],
    template: `
    @switch (view()) {

      @case ('sign-in') {
        <div class="card shadow border-0 card-rounded">
          <div class="card-body account-step">
            @if (error()) {
              <div class="alert alert-danger mb-3">{{ error() }}</div>
            }

            <div class="welcome-hero">
              <h5 class="welcome-title"><i class="bi bi-trophy-fill welcome-icon"></i> Let's Register Your Teams!</h5>
              <p class="wizard-tip">Sign in with your Club Rep credentials to register teams for this event.</p>
            </div>

            <app-login
              [theme]="''"
              [embedded]="true"
              [headerText]="'Club Rep Sign In'"
              [subHeaderText]="'Enter your username and password'"
              [returnUrl]="returnUrl()"
              (loginSuccess)="continueWithLogin()" />

            <div class="or-divider">or</div>

            <div class="create-cta">
              <p>Don't have a club rep account yet?</p>
              <button type="button"
                      class="btn btn-outline-primary fw-semibold w-100"
                      (click)="view.set('create')">
                <i class="bi bi-shield-plus me-2"></i>Create NEW Club Rep Account
              </button>
            </div>
          </div>
        </div>
      }

      @case ('create') {
        <div class="account-step account-step--wide">
          <button type="button" class="back-link" (click)="goHome()">
            <i class="bi bi-arrow-left"></i> Back to home
          </button>
          <div class="card shadow border-0 card-rounded">
            <div class="card-header card-header-subtle border-0 py-3 create-header">
              <h5 class="mb-0">
                <i class="bi bi-shield-plus"></i>
                Create Club Rep Account
                <span class="new-pill">NEW</span>
              </h5>
            </div>
            <div class="card-body bg-neutral-0">
              <p class="wizard-tip">Set up your permanent Club Team Library — register teams once, reuse at every future event.</p>
              <app-club-rep-register-form
                mode="create"
                (registered)="onRegistered()" />
            </div>
          </div>
        </div>
      }

      @case ('account-summary') {
        <div class="account-step account-step--wide">
          <button type="button" class="back-link" (click)="goHome()">
            <i class="bi bi-arrow-left"></i> Back to home
          </button>
          <div class="card shadow border-0 card-rounded">
            <div class="card-body">
              @if (loadingProfile()) {
                <div class="summary-loading">
                  <span class="spinner-border spinner-border-sm me-2"></span>Loading your profile...
                </div>
              } @else {
                <div class="summary-identity">
                  <i class="bi bi-check-circle-fill"></i>
                  <div>
                    <div class="name">{{ displayName() }}</div>
                    <span class="email">{{ profileForEdit()?.email ?? auth.getCurrentUser()?.username }}</span>
                  </div>
                </div>
                <div class="summary-actions">
                  <button type="button" class="btn btn-outline-primary" (click)="onEditClick()">
                    <i class="bi bi-pencil me-1"></i>Edit Profile
                  </button>
                  <button type="button" class="btn btn-outline-secondary" (click)="onSignOut()">
                    <i class="bi bi-box-arrow-right me-1"></i>Sign Out
                  </button>
                </div>
              }
            </div>
          </div>
        </div>
      }

      @case ('edit-profile') {
        <div class="account-step account-step--wide">
          <button type="button" class="back-link" (click)="view.set('account-summary')">
            <i class="bi bi-arrow-left"></i> Back
          </button>
          <div class="card shadow border-0 card-rounded">
            <div class="card-header card-header-subtle border-0 py-3 edit-header">
              <h5 class="mb-0">
                <i class="bi bi-person-gear"></i>
                Edit Profile
              </h5>
            </div>
            <div class="card-body bg-neutral-0">
              <app-club-rep-register-form
                mode="edit"
                [existing]="profileForEdit()"
                (saved)="onProfileSaved()" />
            </div>
          </div>
        </div>
      }

    }
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamLoginStepComponent implements OnInit {
    /** Roles that are valid for team registration */
    private static readonly ALLOWED_ROLES: ReadonlySet<string> = new Set([Roles.ClubRep]);

    readonly loginSuccess = output<LoginStepResult>();
    readonly registrationSuccess = output<LoginStepResult>();

    readonly auth = inject(AuthService);
    private readonly clubService = inject(ClubService);
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly router = inject(Router);
    private readonly destroyRef = inject(DestroyRef);

    readonly error = signal<string | null>(null);
    readonly view = signal<LoginView>('sign-in');
    readonly profileForEdit = signal<ClubRepProfileDto | null>(null);
    readonly loadingProfile = signal(false);

    ngOnInit(): void {
        if (this.auth.isAuthenticated()) {
            const role = this.auth.currentUser()?.role;
            if (role && !TeamLoginStepComponent.ALLOWED_ROLES.has(role)) {
                this.auth.logoutLocal();
                return;
            }
            // Authenticated club rep landed back on this tab — show summary view.
            this.view.set('account-summary');
            this.loadProfile();
        }
    }

    returnUrl(): string {
        const jobPath = this.state.jobPath();
        return jobPath ? `/${jobPath}/registration/team` : '/tsic/role-selection';
    }

    /** Display name from loaded profile, with username fallback. */
    displayName(): string {
        const p = this.profileForEdit();
        if (p && (p.firstName || p.lastName)) {
            return `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim();
        }
        return this.auth.getCurrentUser()?.username ?? '';
    }

    /** Called by inline create form after ToS accepted — user is already authenticated. */
    onRegistered(): void {
        this.continueWithLogin();
    }

    onEditClick(): void {
        // Refetch on each edit click so the form is bound to fresh server state.
        this.loadProfile(() => this.view.set('edit-profile'));
    }

    onProfileSaved(): void {
        // Refetch so the summary reflects what was just persisted.
        this.loadProfile(() => this.view.set('account-summary'));
    }

    onSignOut(): void {
        this.auth.logoutLocal();
        this.profileForEdit.set(null);
        this.error.set(null);
        // Exit the wizard entirely. Re-entering from home gives a clean sign-in start;
        // looping back to the sign-in view inside the wizard feels like a restart, not an exit.
        this.goHome();
    }

    /** Exit the wizard back to the public job landing page. */
    goHome(): void {
        const jobPath = this.state.jobPath();
        if (jobPath) this.router.navigate(['/', jobPath]);
    }

    private loadProfile(onSuccess?: () => void): void {
        this.loadingProfile.set(true);
        this.clubService.getSelfProfile()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (profile) => {
                    this.profileForEdit.set(profile);
                    this.loadingProfile.set(false);
                    onSuccess?.();
                },
                error: () => {
                    this.loadingProfile.set(false);
                    // Stay on summary; display falls back to username.
                    onSuccess?.();
                },
            });
    }

    continueWithLogin(): void {
        this.error.set(null);

        // Guard: reject non-ClubRep logins (check role if present in token)
        const user = this.auth.currentUser();
        if (user?.role && !TeamLoginStepComponent.ALLOWED_ROLES.has(user.role)) {
            this.auth.logoutLocal();
            this.error.set('This is not a club rep account. Please sign in with a club rep account or create one below.');
            return;
        }

        this.teamReg.getMyClubs()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (clubs: ClubRepClubDto[]) => {
                    if (!clubs.length) {
                        this.auth.logoutLocal();
                        this.error.set('No clubs found for this account. This is not a club rep account, or your club has not been set up yet.');
                        return;
                    }
                    const clubName = clubs.length === 1 ? clubs[0].clubName : null;
                    this.loginSuccess.emit({ availableClubs: clubs, clubName });
                },
                error: (err: unknown) => {
                    const httpErr = err as { status?: number };
                    console.error('[TeamLogin] Failed to load clubs', err);
                    this.auth.logoutLocal();
                    if (httpErr?.status === 403) {
                        this.error.set('This is not a club rep account. Please sign in with a club rep account or create one below.');
                    } else {
                        this.error.set('Failed to load your clubs. Please try again.');
                    }
                },
            });
    }
}
