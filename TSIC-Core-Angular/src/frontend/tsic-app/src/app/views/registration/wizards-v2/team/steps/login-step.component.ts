import { ChangeDetectionStrategy, Component, inject, OnInit, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/wizards/team-registration-wizard/services/team-registration.service';
import { LoginComponent } from '@views/auth/login/login.component';
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
    imports: [FormsModule, LoginComponent],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Club Rep Login</h5>
      </div>
      <div class="card-body">
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
          <p class="text-muted mb-3">
            Sign in with your Club Rep credentials to register teams for this event.
          </p>
          @if (error()) {
            <div class="alert alert-danger">{{ error() }}</div>
          }
          <div class="row g-3 align-items-stretch">
            <div class="col-12 col-md-6 d-flex">
              <app-login class="flex-fill"
                [theme]="''"
                [embedded]="true"
                [headerText]="'Club Rep Sign In'"
                [subHeaderText]="'Sign in with your club rep account'"
                [returnUrl]="returnUrl()" />
            </div>
            <div class="col-12 col-md-6 d-flex">
              <div class="card border rounded flex-fill" style="border-color: var(--border-color); border-radius: var(--radius-lg); box-shadow: var(--shadow-lg); background: var(--brand-surface);">
                <div class="card-body d-flex flex-column justify-content-center align-items-center text-center"
                     style="padding: var(--space-6);">
                  <i class="bi bi-shield-fill-plus" style="font-size: 2.5rem; color: var(--bs-primary); margin-bottom: var(--space-4);"></i>
                  <h5 class="fw-bold mb-2" style="color: var(--brand-text);">New Club Rep?</h5>
                  <p class="mb-4" style="color: var(--brand-text-muted); font-size: var(--font-size-sm);">
                    Club rep accounts are created by your league administrator. Contact them to get your credentials.
                  </p>
                  <div class="d-flex align-items-center gap-2" style="color: var(--brand-text-muted); font-size: var(--font-size-sm);">
                    <i class="bi bi-envelope-fill" style="color: var(--bs-primary);"></i>
                    <span>Reach out to your league admin</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        }
      </div>
    </div>
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
        return jobPath ? `/${jobPath}/register-team` : '/tsic/role-selection';
    }

    continueWithLogin(): void {
        this.error.set(null);
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
