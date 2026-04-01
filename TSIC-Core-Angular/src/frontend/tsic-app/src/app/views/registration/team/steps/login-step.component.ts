import { ChangeDetectionStrategy, Component, inject, OnInit, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { ClubRepRegisterFormComponent } from './club-rep-register-form.component';
import type { ClubRepClubDto } from '@core/api';

export interface LoginStepResult {
    availableClubs: ClubRepClubDto[];
    clubName: string | null;
}

/**
 * Team Login step — login/register form for club reps.
 * Delegates authentication to ClubRepWorkflowService.
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

      @media (max-width: 575.98px) {
        .account-step { padding-top: var(--space-4); }
        .or-divider { margin: var(--space-2) 0; font-size: var(--font-size-xs); }
        .create-cta p { font-size: var(--font-size-xs); margin-bottom: var(--space-2); }
      }
    `],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body account-step">
        @if (auth.isAuthenticated() && auth.hasSelectedRole()) {
          <div class="alert alert-success d-flex align-items-start" role="alert">
            <i class="bi bi-check-circle me-2 mt-1"></i>
            <div>
              Signed in as <strong>{{ auth.getCurrentUser()?.username }}</strong>.
              <button type="button" class="btn btn-sm btn-primary ms-3" (click)="continueWithLogin()">
                Continue to Teams
              </button>
            </div>
          </div>
        } @else {
          @if (error()) {
            <div class="alert alert-danger mb-3">{{ error() }}</div>
          }

          <p class="wizard-tip">Sign in with your Club Rep credentials to register teams for this event.</p>

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
                    (click)="showRegisterModal.set(true)">
              <i class="bi bi-shield-plus me-2"></i>Create Club Rep Account
            </button>
          </div>
        }
      </div>
    </div>

    @if (showRegisterModal()) {
      <app-club-rep-register-form
        (registered)="onRegistered($event)"
        (closed)="showRegisterModal.set(false)" />
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
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    readonly error = signal<string | null>(null);
    readonly showRegisterModal = signal(false);

    ngOnInit(): void {
        if (this.auth.isAuthenticated()) {
            const role = this.auth.currentUser()?.role;
            if (role && !TeamLoginStepComponent.ALLOWED_ROLES.has(role)) {
                this.auth.logoutLocal();
            }
        }
    }

    returnUrl(): string {
        const jobPath = this.state.jobPath();
        return jobPath ? `/${jobPath}/registration/team` : '/tsic/role-selection';
    }

    onRegistered(_credentials: { username: string; password: string }): void {
        // Registration complete — user sees success state on the form
        // and can now sign in with the login form on the left.
        // No auto-login needed; the login component is right there.
    }

    continueWithLogin(): void {
        this.error.set(null);

        // Guard: reject non-ClubRep logins
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
                    const clubName = clubs.length === 1 ? clubs[0].clubName : null;
                    this.loginSuccess.emit({ availableClubs: clubs, clubName });
                },
                error: (err: unknown) => {
                    console.error('[TeamLogin] Failed to load clubs', err);
                    this.error.set('Failed to load your clubs. Please try again.');
                },
            });
    }
}
