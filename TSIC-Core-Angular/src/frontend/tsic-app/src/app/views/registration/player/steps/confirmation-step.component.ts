import { ChangeDetectionStrategy, Component, inject, computed, signal, output, OnInit, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { environment } from '@environments/environment';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import type { JobPulseDto } from '@core/api';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { RichTextPipe } from '@infrastructure/pipes/rich-text.pipe';

/**
 * Confirmation step — displays the server-rendered confirmation HTML,
 * allows resending the confirmation email, and shows a "Return Home" button.
 */
@Component({
    selector: 'app-prw-confirmation-step',
    standalone: true,
    imports: [RouterLink, TestSendButtonComponent, RichTextPipe],
    styles: [`
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

    <!-- Persistent waitlist notice: any players the server couldn't seat (their team filled up
         before this payment) were moved to the waitlist at $0 and NOT charged. Unlike the old
         auto-dismissing toast, this stays on screen so the family can't miss which kids still
         need to finish waitlist signup. -->
    @if (waitlisted().length > 0) {
      <div class="alert alert-warning d-flex align-items-start gap-2 mb-3" role="alert">
        <i class="bi bi-exclamation-triangle-fill mt-1"></i>
        <div class="flex-grow-1">
          <div class="fw-semibold mb-1">
            {{ waitlisted().length }} player(s) were moved to the waitlist
          </div>
          <div class="small mb-2">
            These team(s) filled up before your payment was processed, so these player(s)
            were <strong>not charged</strong> and were placed on the waitlist. Everyone else
            in your cart was registered and charged successfully.
          </div>
          <ul class="list-unstyled mb-0">
            @for (w of waitlisted(); track w.registrationId) {
              <li class="d-flex align-items-center gap-2 py-1">
                <span class="badge text-bg-warning">WAITLISTED</span>
                <span>{{ w.playerName }} &mdash; {{ w.teamName }}</span>
              </li>
            }
          </ul>
        </div>
      </div>
    }

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
          <!-- ONE button, two behaviors: off-prod the resend couldn't deliver anyway (sandbox
               email gate), so the button IS the test-send popover; on prod it really resends. -->
          <div class="d-flex gap-2 mb-3 align-items-center flex-wrap">
            @if (isNonProd) {
              <app-test-send-button
                label="Re-Send Confirmation Email"
                variant="primary"
                align="left"
                drop="down"
                [busy]="testSending()"
                (send)="onTestSend($event)" />
            } @else {
              <button type="button" class="btn btn-outline-primary"
                      [disabled]="resending()"
                      (click)="onResendClick()">
                {{ resending() ? 'Sending...' : 'Re-Send Confirmation Email' }}
              </button>
            }
          </div>
          @if (resendMessage()) {
            <div class="small text-muted mb-2">{{ resendMessage() }}</div>
          }

          <div class="confirmation-content mt-3" [innerHTML]="conf()!.confirmationHtml | richText"></div>

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
    // Players the server couldn't seat at payment time (team filled up) — placed on the
    // waitlist twin at $0 and not charged. Rendered as a persistent notice above the receipt.
    readonly waitlisted = computed(() => this.state.lastPayment()?.waitlisted ?? []);
    readonly loadError = signal(false);
    readonly resending = signal(false);
    readonly resendMessage = signal('');

    /** Test send is a non-prod affordance; the backend refuses in Production regardless. */
    readonly isNonProd = !environment.production;
    readonly testSending = signal(false);

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

    /** Renders THIS family's confirmation and delivers it to the tester's inbox instead of them. */
    async onTestSend(options: TestSendOptions): Promise<void> {
        if (this.testSending()) return;
        this.resendMessage.set('');
        this.testSending.set(true);
        const result = await this.state.testSendConfirmationEmail(options.recipient);
        this.testSending.set(false);
        this.resendMessage.set(
            result?.sent
                ? `Test confirmation (rendered for ${result.renderedFor}) sent to ${result.recipient}.`
                : result?.message || 'Test send failed.');
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
