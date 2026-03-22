import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
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
      .create-account-card {
        border-color: var(--border-color);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-lg);
        background: var(--brand-surface);
      }
      .create-account-card .card-body {
        padding: var(--space-6);
      }
      .create-account-icon {
        font-size: 2.5rem;
        color: var(--bs-primary);
        margin-bottom: var(--space-4);
      }
      .create-account-card h5 { color: var(--brand-text); }
      .create-account-card p { color: var(--brand-text-muted); font-size: var(--font-size-sm); }
      .create-account-card .btn { border-radius: var(--radius-sm); }
    `],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Family Account</h5>
      </div>
      <div class="card-body">
        @if (loadError()) {
          <div class="alert alert-danger d-flex align-items-start" role="alert">
            <i class="bi bi-exclamation-triangle-fill me-2 mt-1"></i>
            <div>
              <div class="fw-semibold mb-1">Failed to load player data</div>
              <div class="small">{{ loadError() }}</div>
              <button type="button" class="btn btn-sm btn-outline-danger mt-2" (click)="retryLoad()">Retry</button>
            </div>
          </div>
        } @else if (auth.isAuthenticated()) {
          <div class="alert alert-success d-flex align-items-start" role="alert">
            <i class="bi bi-check-circle me-2 mt-1"></i>
            <div>
              Signed in as <strong>{{ auth.getCurrentUser()?.username }}</strong>.
              <button type="button" class="btn btn-sm btn-primary ms-3" (click)="onContinue()"
                      [disabled]="loading()">
                {{ loading() ? 'Loading...' : 'Continue' }}
              </button>
            </div>
          </div>
        } @else {
          <p class="text-muted">
            Sign in with your family account to register players.
            If you don't have a family account yet, create one first.
          </p>
          <div class="row g-3 align-items-stretch">
            <div class="col-12 col-md-6 d-flex">
              <app-login class="flex-fill"
                [theme]="'player'"
                [embedded]="true"
                [headerText]="'Sign In'"
                [subHeaderText]="'Sign in with your family account'"
                [returnUrl]="returnUrl()" />
            </div>
            <div class="col-12 col-md-6 d-flex">
              <div class="card border rounded flex-fill create-account-card">
                <div class="card-body d-flex flex-column justify-content-center align-items-center text-center">
                  <i class="bi bi-people-fill create-account-icon"></i>
                  <h5 class="fw-bold mb-2">New to Registration?</h5>
                  <p class="mb-4">
                    Create a family account to get started. You'll use it to register players for this event.
                  </p>
                  <button type="button" class="btn btn-primary btn-lg fw-semibold w-100" (click)="goToFamilyWizard()">
                    Create Family Account
                  </button>
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
export class FamilyCheckStepComponent implements OnInit {
    /** Roles that are valid for player registration */
    private static readonly ALLOWED_ROLES: ReadonlySet<string> = new Set([Roles.Family, Roles.Player]);

    readonly advance = output<void>();
    readonly auth = inject(AuthService);
    private readonly router = inject(Router);
    private readonly jobService = inject(JobService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly state = inject(PlayerWizardStateService);

    readonly loading = signal(false);
    readonly loadError = signal<string | null>(null);

    ngOnInit(): void {
        if (this.auth.isAuthenticated()) {
            const role = this.auth.currentUser()?.role;
            if (role && !FamilyCheckStepComponent.ALLOWED_ROLES.has(role)) {
                this.auth.logoutLocal();
            }
        }
    }

    returnUrl(): string {
        const jobPath = this.jobService.getCurrentJob()?.jobPath;
        return jobPath ? `/${jobPath}/register-player` : '/tsic/role-selection';
    }

    onContinue(): void {
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
                    this.advance.emit();
                },
                error: (err: unknown) => {
                    this.loading.set(false);
                    console.error('[FamilyCheck] setWizardContext failed', err);
                    this.loadError.set('Could not load player data. Please check your connection and try again.');
                },
            });
    }

    goToFamilyWizard(): void {
        const jobPath = this.jobService.getCurrentJob()?.jobPath || '';
        this.router.navigate([`/${jobPath}/family-account`], {
            queryParams: { next: 'register-player' },
        });
    }
}
