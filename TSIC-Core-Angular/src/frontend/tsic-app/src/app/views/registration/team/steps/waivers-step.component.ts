import {
    ChangeDetectionStrategy, Component, DestroyRef,
    inject, signal, computed, output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * Team Waivers step — displays refund policy HTML, requires checkbox acceptance.
 * Calls accept-refund-policy endpoint to record BWaiverSigned3 = true.
 * Only shown when the job has a refund policy configured.
 */
@Component({
    selector: 'app-trw-waivers-step',
    standalone: true,
    imports: [],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Refund Terms &amp; Conditions</h5>
      </div>
      <div class="card-body">
        <p class="wizard-tip">Before getting started, please review and accept the refund policy for this event.</p>

        <div class="policy-content" [innerHTML]="policyHtml()"></div>

        <div class="acceptance-row">
          <input id="acceptRefund" type="checkbox"
                 class="form-check-input"
                 [checked]="isAccepted()"
                 [disabled]="saving()"
                 (change)="onToggle($any($event.target).checked)" />
          <label for="acceptRefund" class="acceptance-label">
            I have read and agree to the Refund Terms and Conditions
          </label>
        </div>

        @if (error()) {
          <div class="alert alert-danger py-2 mt-2 mb-0 small">{{ error() }}</div>
        }
      </div>
    </div>
  `,
    styles: [`
    .policy-content {
      max-height: 300px;
      overflow-y: auto;
      padding: var(--space-3);
      border: 1px solid var(--border-color);
      border-radius: var(--radius-sm);
      background: var(--bs-tertiary-bg);
      font-size: var(--font-size-sm);
      line-height: var(--line-height-relaxed);
      margin-bottom: var(--space-4);
    }

    .acceptance-row {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
      padding: var(--space-3);
      border: 1px solid var(--border-color);
      border-radius: var(--radius-sm);
      background: rgba(var(--bs-primary-rgb), 0.03);
    }

    .acceptance-label {
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
      color: var(--brand-text);
      cursor: pointer;
      line-height: var(--line-height-normal);
    }
  `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamWaiversStepComponent {
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly policyHtml = computed(() => this.state.refundPolicyHtml() ?? '');
    readonly isAccepted = computed(() => this.state.waiverAccepted());
    readonly saving = signal(false);
    readonly error = signal<string | null>(null);

    onToggle(checked: boolean): void {
        if (checked && !this.state.waiverAccepted()) {
            this.recordAcceptance();
        } else if (!checked) {
            this.state.setWaiverAccepted(false);
        }
    }

    private recordAcceptance(): void {
        this.saving.set(true);
        this.error.set(null);

        this.teamReg.acceptRefundPolicy()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.state.setWaiverAccepted(true);
                },
                error: () => {
                    this.saving.set(false);
                    this.error.set('Failed to record acceptance. Please try again.');
                    this.toast.show('Failed to record waiver acceptance.', 'danger', 4000);
                },
            });
    }
}
