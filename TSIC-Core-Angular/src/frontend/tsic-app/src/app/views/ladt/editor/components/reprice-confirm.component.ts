import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

/** The reprice prompt's state (mirrors each fee panel's `repriceDialog` signal). */
export interface RepriceDialog {
  isPhase: boolean;
  message: string;
  /**
   * Present only for an age-group PHASE flip that can fan out across its league: offers a
   * "this age group vs all age groups in the league" scope choice with each side's count.
   */
  leagueScope?: { thisCount: number; allCount: number; unit: string } | null;
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
      @if (dialog().isPhase && dialog().leagueScope; as scope) {
        <div class="reprice-scope" role="radiogroup" aria-label="Apply payment phase to">
          <span class="reprice-scope-legend" aria-hidden="true">{{ scope.unit }}</span>
          <label class="reprice-scope-opt" [class.selected]="selectedScope() === 'all'">
            <input type="radio" name="repriceScope" [checked]="selectedScope() === 'all'"
                   (change)="selectedScope.set('all')">
            <span class="reprice-scope-text">Apply to all age groups in this league</span>
            <span class="reprice-scope-count" [attr.aria-label]="scope.allCount + ' ' + scope.unit">{{ scope.allCount }}</span>
          </label>
          <label class="reprice-scope-opt" [class.selected]="selectedScope() === 'this'">
            <input type="radio" name="repriceScope" [checked]="selectedScope() === 'this'"
                   (change)="selectedScope.set('this')">
            <span class="reprice-scope-text">Just this age group</span>
            <span class="reprice-scope-count" [attr.aria-label]="scope.thisCount + ' ' + scope.unit">{{ scope.thisCount }}</span>
          </label>
        </div>
      }
      <div class="reprice-confirm-actions">
        @if (dialog().isPhase) {
          <button type="button" class="btn btn-sm btn-warning" autofocus (click)="onConvert()">Convert</button>
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

    .reprice-scope {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }
    /* Small caption clarifying that each option's right-hand number is a count of
       existing registrations (players + teams) the conversion will touch. */
    .reprice-scope-legend {
      align-self: flex-end;
      padding-right: var(--space-3);
      font-size: var(--font-size-2xs);
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--text-muted);
    }
    .reprice-scope-opt {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-3);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-md, 0.5rem);
      cursor: pointer;
      font-size: var(--font-size-sm);
      color: var(--bs-body-color);
      transition: border-color 0.12s ease, background-color 0.12s ease;
    }
    .reprice-scope-opt.selected {
      border-color: var(--bs-primary);
      background: rgba(var(--bs-primary-rgb), 0.08);
    }
    .reprice-scope-opt input { accent-color: var(--bs-primary); margin: 0; }
    .reprice-scope-text { flex: 1; }
    .reprice-scope-count {
      flex-shrink: 0;
      min-width: 1.75rem;
      padding: 0 var(--space-2);
      text-align: center;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      color: var(--bs-primary-text-emphasis);
      background: rgba(var(--bs-primary-rgb), 0.12);
      border-radius: 999px;
    }
    .reprice-scope-opt:focus-within {
      outline: none;
      box-shadow: var(--shadow-focus);
    }
    @media (prefers-reduced-motion: reduce) {
      .reprice-scope-opt { transition: none; }
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
  /**
   * Primary, retroactive action for a PHASE flip — "Convert". Carries the chosen scope:
   * 'all' fans the phase across every age group in the league; 'this' is the single age
   * group. Always 'this' when no league-scope selector is shown (league/team panels).
   */
  readonly convert = output<'this' | 'all'>();
  /** Secondary — "Future only" (amount, still saves) / "Cancel" (phase, reverts the toggle). */
  readonly secondary = output<void>();
  /** Back-out — collapse and save nothing (amount change only). */
  readonly keepEditing = output<void>();

  /** Selected fan-out scope; only meaningful when `dialog().leagueScope` is present.
   *  Defaults to 'all' (the whole league) — flipping the final-balance-due phase is almost
   *  always meant to land on every age group at once, so that's the expected default. The
   *  rep can still narrow to 'this' age group when they want the single-group change. */
  readonly selectedScope = signal<'this' | 'all'>('all');

  onConvert(): void {
    this.convert.emit(this.dialog().leagueScope ? this.selectedScope() : 'this');
  }
}
