import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { TosContentComponent } from './tos-content.component';

/**
 * Shared Terms of Service acceptance step for registration wizards.
 * Pure presentation — does NOT inject AuthService. Parent wizard handles
 * the actual acceptTos() API call when (accepted) fires.
 */
@Component({
    selector: 'app-tos-acceptance-step',
    standalone: true,
    imports: [TosContentComponent],
    template: `
    <div class="tos-wizard-step">

      <!-- Header -->
      <div class="tos-header-card">
        <h5 class="tos-title">Terms of Service</h5>
        <span class="tos-updated">
          <i class="bi bi-calendar-event"></i>
          Updated December 31, 2025
        </span>
      </div>

      <!-- Document -->
      <div class="tos-card">
        <div class="tos-scroll">
          <app-tos-content />
        </div>
      </div>

      <!-- Acceptance -->
      <div class="tos-accept-card">
        @if (error()) {
          <div class="alert alert-danger d-flex align-items-start mb-3" role="alert">
            <i class="bi bi-exclamation-triangle-fill me-2 mt-1"></i>
            <span>{{ error() }}</span>
          </div>
        }

        <label class="tos-checkbox" for="tosAcceptWizard">
          <input type="checkbox" id="tosAcceptWizard" [checked]="checked()"
                 (change)="checked.set(!checked())" [disabled]="submitting()">
          <span>I have read and agree to the <strong>Terms of Service</strong></span>
        </label>

        <button type="button" class="btn btn-primary btn-lg w-100 tos-submit"
                [disabled]="!checked() || submitting()" (click)="accepted.emit()">
          @if (submitting()) {
            <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
            <span>Accepting...</span>
          } @else {
            <span>Accept and Continue</span>
          }
        </button>
      </div>

    </div>
  `,
    styles: [`
      :host { display: block; }

      .tos-wizard-step {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
      }

      .tos-header-card {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: var(--space-2);
        padding: var(--space-4) var(--space-5);
        background: var(--brand-surface);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-xs);

        @media (max-width: 575.98px) { padding: var(--space-3); }
      }

      .tos-title {
        font-size: var(--font-size-xl);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        margin: 0;
      }

      .tos-updated {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-warning-rgb), 0.1);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.2);
      }

      .tos-card {
        background: var(--brand-surface);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-xs);
      }

      .tos-scroll {
        max-height: 420px;
        overflow-y: auto;
        padding: var(--space-5);

        @media (max-width: 575.98px) {
          max-height: 350px;
          padding: var(--space-3);
        }
      }

      .tos-accept-card {
        background: var(--brand-surface);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-sm);
        padding: var(--space-4) var(--space-5);

        @media (max-width: 575.98px) { padding: var(--space-3); }
      }

      .tos-checkbox {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        margin-bottom: var(--space-4);
        cursor: pointer;
        user-select: none;
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        line-height: 1.4;

        input[type="checkbox"] {
          flex-shrink: 0;
          width: 20px;
          height: 20px;
          margin-top: 1px;
          accent-color: var(--bs-primary);
          cursor: pointer;
        }
      }

      .tos-submit {
        border-radius: var(--radius-sm);
        font-weight: var(--font-weight-semibold);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TosAcceptanceStepComponent {
    readonly submitting = input(false);
    readonly error = input<string | null>(null);
    readonly accepted = output<void>();
    readonly checked = signal(false);
}
