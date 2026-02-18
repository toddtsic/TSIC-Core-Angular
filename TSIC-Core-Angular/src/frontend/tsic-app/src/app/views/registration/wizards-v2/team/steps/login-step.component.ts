import { ChangeDetectionStrategy, Component, inject, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
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
          <div class="row g-3">
            <div class="col-12 col-md-6">
              <app-login
                [theme]="''"
                [headerText]="'Club Rep Sign In'"
                [subHeaderText]="'Sign in with your club rep account'"
                [returnUrl]="returnUrl()" />
            </div>
            <div class="col-12 col-md-6">
              <div class="card border rounded bg-body-tertiary h-100">
                <div class="card-body d-flex flex-column justify-content-center align-items-center">
                  <h6 class="fw-semibold mb-2">New Club Rep?</h6>
                  <p class="text-muted small text-center mb-3">
                    Contact your league administrator to get club rep credentials.
                  </p>
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
export class TeamLoginStepComponent {
    readonly loginSuccess = output<LoginStepResult>();
    readonly registrationSuccess = output<LoginStepResult>();

    readonly auth = inject(AuthService);
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    readonly error = signal<string | null>(null);

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
