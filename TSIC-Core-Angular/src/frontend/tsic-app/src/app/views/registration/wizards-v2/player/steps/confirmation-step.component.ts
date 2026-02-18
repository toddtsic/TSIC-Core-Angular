import { ChangeDetectionStrategy, Component, inject, computed, signal, output, OnInit, OnDestroy } from '@angular/core';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';

/**
 * Confirmation step — displays the server-rendered confirmation HTML,
 * allows resending the confirmation email, and shows a "Finish" button.
 */
@Component({
    selector: 'app-prw-confirmation-step',
    standalone: true,
    imports: [],
    styles: [`
    .confirmation-content { overflow-x: auto; }
    .confirmation-content ::ng-deep table { width: 100%; min-width: 600px; }
  `],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Confirmation</h5>
      </div>
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

          <button type="button" class="btn btn-primary mt-3" (click)="finished.emit()">Finish</button>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmationStepComponent implements OnInit, OnDestroy {
    readonly finished = output<void>();
    private readonly state = inject(PlayerWizardStateService);

    private pollTimer: ReturnType<typeof setInterval> | null = null;
    private safetyTimer: ReturnType<typeof setTimeout> | null = null;

    readonly conf = computed(() => this.state.confirmation());
    readonly confirmationLoaded = computed(() => !!this.conf());
    readonly loadError = signal(false);
    readonly resending = signal(false);
    readonly resendMessage = signal('');

    ngOnInit(): void {
        this.startLoading();
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
