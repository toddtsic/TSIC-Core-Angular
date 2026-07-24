import { ChangeDetectionStrategy, Component, inject, signal, computed, output, OnInit, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * Team Review step — shows confirmation HTML, resend email, finish button.
 */
@Component({
    selector: 'app-trw-review-step',
    standalone: true,
    imports: [],
    styles: [`
    .confirmation-content { overflow-x: auto; }
    .confirmation-content ::ng-deep table { width: 100%; min-width: 600px; }
  `],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Review &amp; Confirmation</h5>
      </div>
      <div class="card-body">
        @if (!confirmationLoaded()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading confirmation...</span>
            </div>
            <p class="text-muted mt-2">Loading confirmation...</p>
          </div>
        } @else {
          <!-- Waitlist banner — mirrors the payment step's messaging so the final
               "you're done" screen never lets a waitlisted team pass as registered. -->
          @if (waitlistedTeams().length) {
            <div class="alert alert-warning border-0 mb-3" role="status">
              <div class="d-flex align-items-start gap-2">
                <span class="badge bg-warning text-dark">Waitlisted</span>
                <div>
                  {{ waitlistedTeams().length === 1 ? 'This team is' : 'These teams are' }} on the
                  waitlist: <strong>{{ waitlistedNames() }}</strong>. No payment is due unless the
                  event director places {{ waitlistedTeams().length === 1 ? 'it' : 'them' }} into an
                  open roster spot.
                </div>
              </div>
            </div>
          }

          <div class="d-flex gap-2 mb-3">
            <button type="button" class="btn btn-outline-primary btn-sm"
                    [disabled]="resending()"
                    (click)="onResendClick()">
              {{ resending() ? 'Sending...' : 'Re-Send Confirmation Email' }}
            </button>
          </div>
          @if (resendMessage()) {
            <div class="small text-muted mb-2">{{ resendMessage() }}</div>
          }

          <div class="confirmation-content" [innerHTML]="confirmationHtml()"></div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamReviewStepComponent implements OnInit {
    readonly finished = output<void>();
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly confirmationHtml = signal<string | null>(null);
    readonly confirmationLoaded = computed(() => !!this.confirmationHtml());
    /** Waitlisted teams from the payment ledger (owes $0 until promoted) — the
     *  banner above the confirmation keeps that status explicit at the finish line. */
    readonly waitlistedTeams = computed(() => this.state.teamPayment.teams().filter(t => t.isWaitlisted));
    readonly waitlistedNames = computed(() => this.waitlistedTeams().map(t => t.teamName).join(', '));
    readonly resending = signal(false);
    readonly resendMessage = signal('');

    ngOnInit(): void {
        this.loadConfirmation();
    }

    private loadConfirmation(): void {
        const regId = this.state.clubRep.registrationId();
        if (!regId) return;
        this.teamReg.getConfirmationText(regId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: html => this.confirmationHtml.set(html || '<p>Registration confirmed.</p>'),
                error: () => {
                    // Interceptor safety net shows toast; show inline fallback too.
                    this.confirmationHtml.set('<p class="text-muted">Registration confirmed. Confirmation details could not be loaded.</p>');
                },
            });

        // Auto-send email on load — intentionally silent (no toast on failure).
        // Pass isEcheckPending so the BE prepends the settlement-pending banner when the
        // just-completed payment was an eCheck submission.
        const isEcheckPending = this.state.teamPaymentState.lastPayment()?.paymentMethod === 'Echeck';
        this.teamReg.sendConfirmationEmail(regId, false, skipErrorToast(), isEcheckPending)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ error: () => { /* ignore auto-send failure */ } });
    }

    onResendClick(): void {
        if (this.resending()) return;
        this.resendMessage.set('');
        this.resending.set(true);
        const regId = this.state.clubRep.registrationId();
        if (!regId) { this.resending.set(false); return; }
        const isEcheckPending = this.state.teamPaymentState.lastPayment()?.paymentMethod === 'Echeck';
        this.teamReg.sendConfirmationEmail(regId, true, undefined, isEcheckPending)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.resending.set(false);
                    this.resendMessage.set('Confirmation email sent.');
                },
                error: () => {
                    this.resending.set(false);
                    this.resendMessage.set('Failed to send confirmation email.');
                },
            });
    }
}
