import { ChangeDetectionStrategy, Component, inject, computed, signal, output, OnInit, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { environment } from '@environments/environment';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import type { JobPulseDto } from '@core/api';

/**
 * Confirmation step — displays the server-rendered confirmation HTML,
 * allows resending the confirmation email, and shows a "Finish" button.
 */
@Component({
    selector: 'app-prw-confirmation-step',
    standalone: true,
    imports: [RouterLink],
    styles: [`
    .welcome-hero { display: flex; flex-direction: column; align-items: center; text-align: center; padding: var(--space-4) var(--space-4) var(--space-3); }
    .welcome-title { margin: 0; font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); color: var(--brand-text); }
    .welcome-icon { font-size: var(--font-size-2xl); }
    .welcome-desc { margin: var(--space-2) 0 0; font-size: var(--font-size-xs); color: var(--brand-text-muted); i { color: var(--bs-primary); } }
    .desc-dot { display: inline-block; width: 4px; height: 4px; border-radius: var(--radius-full); background: var(--neutral-300); vertical-align: middle; margin: 0 var(--space-2); }
    .confirmation-content { overflow-x: auto; }
    .confirmation-content ::ng-deep table { width: 100%; min-width: 600px; }
    .store-cta {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      margin-top: 1rem;
      padding: 0.75rem 1.5rem;
      border-radius: 999px;
      text-decoration: none;
      font-weight: 600;
    }
    @media (max-width: 575.98px) { .welcome-title { font-size: var(--font-size-xl); } .desc-dot { display: none; } .welcome-desc i { display: none; } }
  `],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-patch-check-fill welcome-icon" style="color: var(--bs-success)"></i> Registration Complete!</h4>
      <p class="welcome-desc">
        <i class="bi bi-envelope-check me-1"></i>Confirmation email sent
        <span class="desc-dot"></span>
        <i class="bi bi-file-text me-1"></i>Details below
      </p>
    </div>

    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        @if (loadError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2" role="alert">
            <i class="bi bi-exclamation-triangle-fill mt-1"></i>
            <div>
              <div class="fw-semibold mb-1">Unable to load confirmation</div>
              <div class="small">The confirmation data did not load in time. Please try again.</div>
            </div>
          </div>
          <div class="text-center">
            <button type="button" class="btn btn-primary" (click)="retry()">Retry</button>
          </div>
        } @else if (!confirmationLoaded()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading confirmation...</span>
            </div>
            <p class="text-muted mt-2">Loading confirmation summary...</p>
          </div>
        } @else {
          <button type="button" class="btn btn-outline-primary mb-3"
                  [disabled]="resending()"
                  (click)="onResendClick()">
            {{ resending() ? 'Sending...' : 'Re-Send Confirmation Email' }}
          </button>
          @if (resendMessage()) {
            <div class="small text-muted mb-2">{{ resendMessage() }}</div>
          }

          <div class="confirmation-content mt-3" [innerHTML]="conf()!.confirmationHtml"></div>

          @if (showStoreCta()) {
            <a [routerLink]="'../../store'" [relativeTo]="route" class="store-cta btn btn-outline-primary">
              <i class="bi bi-bag-fill me-1"></i>Browse the Store
            </a>
          }
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmationStepComponent implements OnInit, OnDestroy {
    readonly finished = output<void>();
    private readonly state = inject(PlayerWizardStateService);
    private readonly http = inject(HttpClient);
    readonly route = inject(ActivatedRoute);

    private pollTimer: ReturnType<typeof setInterval> | null = null;
    private safetyTimer: ReturnType<typeof setTimeout> | null = null;

    readonly conf = computed(() => this.state.confirmation());
    readonly confirmationLoaded = computed(() => !!this.conf());
    readonly loadError = signal(false);
    readonly resending = signal(false);
    readonly resendMessage = signal('');

    // Store CTA
    readonly showStoreCta = signal(false);

    ngOnInit(): void {
        this.startLoading();
        this.checkStoreAvailability();
    }

    ngOnDestroy(): void {
        this.clearTimers();
    }

    private startLoading(): void {
        const tryLoad = (): boolean => {
            const jobId = this.state.jobCtx.jobId();
            const familyUserId = this.state.familyPlayers.familyUser()?.familyUserId;
            if (jobId && familyUserId) {
                this.state.loadConfirmation();
                return true;
            }
            return false;
        };

        if (!tryLoad()) {
            this.pollTimer = setInterval(() => {
                if (tryLoad()) this.clearTimers();
            }, 250);
            this.safetyTimer = setTimeout(() => {
                this.clearTimers();
                if (!this.confirmationLoaded()) this.loadError.set(true);
            }, 4000);
        }
    }

    private clearTimers(): void {
        if (this.pollTimer) { clearInterval(this.pollTimer); this.pollTimer = null; }
        if (this.safetyTimer) { clearTimeout(this.safetyTimer); this.safetyTimer = null; }
    }

    private checkStoreAvailability(): void {
        const jobPath = this.state.jobCtx.jobPath();
        if (!jobPath) return;

        this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`).subscribe({
            next: pulse => {
                if (pulse.storeEnabled && pulse.storeHasActiveItems) {
                    this.showStoreCta.set(true);
                }
            },
        });
    }

    retry(): void {
        this.loadError.set(false);
        this.startLoading();
    }

    async onResendClick(): Promise<void> {
        if (this.resending()) return;
        this.resendMessage.set('');
        this.resending.set(true);
        const ok = await this.state.resendConfirmationEmail();
        this.resending.set(false);
        this.resendMessage.set(ok ? 'Confirmation email sent.' : 'Failed to send confirmation email.');
    }
}
