import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** The reprice prompt's state (mirrors each fee panel's `repriceDialog` signal). */
export interface RepriceDialog {
  isPhase: boolean;
  message: string;
}

/**
 * Inline replacement for the reprice confirmation modal. Rendered inside the fly-in's
 * sticky save bar (which switches to a column when confirming), so the "update existing
 * registrations?" decision appears in place at the point of the Save action — no overlay.
 *
 * Semantics mirror the former modal:
 *  - Amount/modifier change → Update all (retroactive) | Future only | Keep editing (abort).
 *  - Phase flip            → Convert (always retroactive) | Cancel (reverts the toggle).
 * The parent owns the behaviour; this is presentation + intent only.
 */
@Component({
  selector: 'app-reprice-confirm',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="reprice-confirm" [class.is-phase]="dialog().isPhase">
      <div class="reprice-confirm-head">
        <i class="bi bi-exclamation-triangle-fill reprice-confirm-icon"></i>
        <div class="reprice-confirm-msg" [innerHTML]="dialog().message"></div>
      </div>
      @if (dialog().isPhase) {
        <!-- PL-062 (Ann): after flipping the phase it wasn't obvious Convert is REQUIRED —
             the flip applies to existing registrations only when Convert is clicked. -->
        <div class="tsic-callout tsic-callout--info tsic-callout--block">
          <i class="bi bi-info-circle" aria-hidden="true"></i>
          <span>Click <strong>Convert</strong> to apply this phase to existing registrations — Cancel reverts the change.</span>
        </div>
      }
      <div class="reprice-confirm-actions">
        @if (dialog().isPhase) {
          <button type="button" class="btn btn-sm btn-warning" autofocus (click)="convert.emit()">Convert</button>
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="secondary.emit()">Cancel</button>
        } @else {
          <!-- "Update all" is the default: a fee change is normally meant to reach existing
               registrants. It leads and takes initial focus so Enter applies to all priors. -->
          <button type="button" class="btn btn-sm btn-warning" autofocus (click)="updateAll.emit()">Update all</button>
          <button type="button" class="btn btn-sm btn-outline-primary" (click)="secondary.emit()">Future only</button>
          <button type="button" class="btn btn-sm btn-link reprice-keep" (click)="keepEditing.emit()">
            <i class="bi bi-arrow-left me-1"></i>Keep editing
          </button>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }

    .reprice-confirm {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      animation: reprice-confirm-in 0.18s ease-out;
    }

    .reprice-confirm-head {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
    }
    .reprice-confirm-icon {
      color: var(--bs-warning-text-emphasis);
      font-size: 1.05rem;
      margin-top: 1px;
      flex-shrink: 0;
    }
    .reprice-confirm-msg {
      font-size: var(--font-size-sm);
      color: var(--bs-body-color);
      line-height: 1.4;
    }

    .reprice-confirm-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    .reprice-keep {
      margin-left: auto;
      text-decoration: none;
      color: var(--bs-secondary-color);
    }
    .reprice-keep:hover { color: var(--bs-body-color); }

    @keyframes reprice-confirm-in {
      from { opacity: 0; transform: translateY(6px); }
      to   { opacity: 1; transform: translateY(0); }
    }
    @media (prefers-reduced-motion: reduce) {
      .reprice-confirm { animation: none; }
    }
  `]
})
export class RepriceConfirmComponent {
  readonly dialog = input.required<RepriceDialog>();

  /** Primary, retroactive action for an AMOUNT change — "Update all". */
  readonly updateAll = output<void>();
  /** Primary, retroactive action for a PHASE flip — "Convert". Always this panel's own scope. */
  readonly convert = output<void>();
  /** Secondary — "Future only" (amount, still saves) / "Cancel" (phase, reverts the toggle). */
  readonly secondary = output<void>();
  /** Back-out — collapse and save nothing (amount change only). */
  readonly keepEditing = output<void>();
}
