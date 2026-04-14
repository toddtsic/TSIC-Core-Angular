import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService } from '../services/team.service';
import { LoginComponent } from '../../../auth/login/login.component';

/**
 * Family Check step — first step of the player registration wizard.
 * Determines whether the user has a family account and either:
 *   - Auto-advances if already authenticated
 *   - Shows login/create CTAs
 */
@Component({
    selector: 'app-prw-family-check-step',
    standalone: true,
    imports: [LoginComponent],
    styles: [`
      :host { display: block; }

      .account-step {
        max-width: 460px;
        margin: 0 auto;
        padding-top: var(--space-8);
      }

      /* Authenticated state */
      .auth-banner {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-4);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-success-rgb), 0.08);
        border: 1px solid rgba(var(--bs-success-rgb), 0.2);
      }
      .auth-banner i {
        font-size: var(--font-size-xl);
        color: var(--bs-success);
        flex-shrink: 0;
      }

      /* Error state */
      .error-banner {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-4);
        border-radius: var(--radius-md);
        background: rgba(var(--bs-danger-rgb), 0.08);
        border: 1px solid rgba(var(--bs-danger-rgb), 0.2);
      }
      .error-banner > i {
        font-size: var(--font-size-xl);
        color: var(--bs-danger);
        flex-shrink: 0;
        margin-top: 2px;
      }

      /* Divider */
      .or-divider {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        margin: var(--space-4) 0;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
      }
      @media (max-width: 575.98px) {
        .or-divider {
          margin: var(--space-2) 0;
          font-size: var(--font-size-xs);
        }
        .create-cta p {
          font-size: var(--font-size-xs);
          margin-bottom: var(--space-2);
        }
      }
      .or-divider::before,
      .or-divider::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--border-color);
      }

      /* Create account CTA */
      .create-cta {
        text-align: center;
      }
      .create-cta p {
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        margin-bottom: var(--space-3);
      }
    `],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body account-step">
        @if (loadError()) {
          <div class="error-banner" role="alert">
            <i class="bi bi-exclamation-triangle-fill"></i>
            <div>
              <div class="fw-semibold mb-1">Failed to load player data</div>
              <div class="small text-muted">{{ loadError() }}</div>
              <button type="button" class="btn btn-sm btn-outline-danger mt-2" (click)="retryLoad()">
                Retry
              </button>
            </div>
          </div>
        } @else if (auth.isAuthenticated()) {
          <div class="auth-banner">
            <span class="spinner-border spinner-border-sm text-primary"></span>
            <span>Loading player data for <strong>{{ auth.getCurrentUser()?.username }}</strong>...</span>
          </div>
        } @else {
          @if (wizardError()) {
            <div class="alert alert-danger d-flex align-items-start gap-2 mb-3" role="alert">
              <i class="bi bi-exclamation-triangle-fill mt-1"></i>
              <span>{{ wizardError() }}</span>
            </div>
          }

          <div class="welcome-hero">
            <h5 class="welcome-title"><i class="bi bi-people-fill welcome-icon"></i> Let's Register Your Players!</h5>
            <p class="wizard-tip">Sign in with your family account to get started.</p>
          </div>

          <app-login
            [theme]="'player'"
            [embedded]="true"
            [headerText]="'Family Account Sign In'"
            [subHeaderText]="'Enter your username and password'"
            [returnUrl]="returnUrl()"
            (loginSuccess)="onContinue()" />

          <div class="or-divider">or</div>

          <div class="create-cta">
            <p class="text-center fw-semibold mb-2 mt-1">Don't have a family account yet?</p>
            <button type="button"
                    class="btn btn-outline-primary fw-semibold w-100"
                    (click)="goToFamilyWizard()">
              <i class="bi bi-people-fill me-2"></i>Create NEW Family Account
            </button>
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyCheckStepComponent implements OnInit {

    readonly advance = output<void>();
    readonly auth = inject(AuthService);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly state = inject(PlayerWizardStateService);
    private readonly teamService = inject(TeamService);

    readonly loading = signal(false);
    readonly loadError = signal<string | null>(null);
    readonly wizardError = signal<string | null>(null);

    ngOnInit(): void {
        // Reset local UI state (may persist from prior navigation)
        this.loading.set(false);
        this.loadError.set(null);
        this.wizardError.set(null);

        // Parent wizard calls logoutLocal() on init for non-Family roles.
        // Only auto-advance if still authenticated as Family/Player.
        if (this.auth.isAuthenticated()) {
            const role = this.auth.currentUser()?.role;
            if (role === 'Family' || role === 'Player') {
                this.onContinue();
            }
        }
    }

    returnUrl(): string {
        const jobPath = this.jobService.getCurrentJob()?.jobPath;
        return jobPath ? `/${jobPath}/registration/player` : '/tsic/role-selection';
    }

    onContinue(): void {
        // Verify the logged-in account is a Family/Player — reject other roles
        const user = this.auth.currentUser();
        if (user?.role && user.role !== 'Family' && user.role !== 'Player') {
            this.auth.logoutLocal();
            this.wizardError.set('This is not a valid family account. Please sign in with a family account or create one below.');
            return;
        }

        this.state.familyPlayers.setHasFamilyAccount('yes');
        const jobPath = this.jobService.getCurrentJob()?.jobPath || '';
        if (jobPath) {
            this.loadWizardContext(jobPath);
        } else {
            this.advance.emit();
        }
    }

    retryLoad(): void {
        this.loadError.set(null);
        const jobPath = this.jobService.getCurrentJob()?.jobPath || '';
        if (jobPath) {
            this.loadWizardContext(jobPath);
        }
    }

    private loadWizardContext(jobPath: string): void {
        this.loading.set(true);
        this.loadError.set(null);
        const apiBase = this.state.jobCtx.resolveApiBase();
        this.state.familyPlayers.setWizardContext(jobPath, apiBase)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.loading.set(false);
                    this.state.initialize(jobPath);
                    this.teamService.loadForJob(jobPath);
                    this.advance.emit();
                },
                error: (err: unknown) => {
                    this.loading.set(false);
                    console.error('[FamilyCheck] setWizardContext failed', err);

                    // Extract server message if available
                    const httpErr = err as { error?: { message?: string }; status?: number };
                    const serverMsg = httpErr?.error?.message;

                    // 400 with a message = business rule (not a family account, etc.)
                    if (httpErr?.status === 400 && serverMsg) {
                        this.auth.logoutLocal();
                        this.wizardError.set(
                            'This is not a valid family account. Please sign in with a family account or create one below.',
                        );
                        return;
                    }

                    this.loadError.set('Could not load player data. Please check your connection and try again.');
                },
            });
    }

    goToFamilyWizard(): void {
        const jobPath = this.jobService.getCurrentJob()?.jobPath || '';
        this.router.navigate([`/${jobPath}/registration/family`], {
            queryParams: { next: 'registration/player' },
        });
    }
}
