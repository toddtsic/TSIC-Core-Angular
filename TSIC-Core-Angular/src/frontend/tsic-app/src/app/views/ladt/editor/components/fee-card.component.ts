import { ChangeDetectionStrategy, Component, Input, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface ModifierForm {
  feeModifierId?: string | null;
  modifierType: string;
  amount: number | null;
  startDate: string | null;
  endDate: string | null;
}

@Component({
  selector: 'app-fee-card',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="section-card"
         [class.fee-card-player]="variant() === 'player'"
         [class.fee-card-clubrep]="variant() === 'clubrep'">
      <div class="section-card-header">
        <i class="bi {{ headerIcon() }}"></i> {{ header() }}
      </div>

      @if (hintText) {
        <p class="fee-hint">{{ hintText }}</p>
      }

      @if (showBaseFee()) {
        <div class="fee-row">
          <div class="fee-field">
            <label class="fee-label">Deposit</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="deposit()" (ngModelChange)="depositChange.emit($event)"
                     [name]="namePrefix() + 'Deposit'"
                     [placeholder]="placeholder()">
            </div>
          </div>
          <div class="fee-field">
            <label class="fee-label">Balance Due</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="balanceDue()" (ngModelChange)="balanceDueChange.emit($event)"
                     [name]="namePrefix() + 'BalanceDue'"
                     [placeholder]="placeholder()">
            </div>
          </div>
        </div>

      }

      @if (showPhaseToggle() || phaseExplanation() || phaseNote()) {
        <div class="phase-section">
          <label class="fee-label phase-section-label">Payment Phase</label>
          @if (showPhaseToggle()) {
            <div class="form-check form-switch mb-0">
              <input class="form-check-input" type="checkbox" role="switch"
                     [id]="namePrefix() + 'Phase'"
                     [checked]="bFullPaymentRequired() === true"
                     (change)="onPhaseToggle($any($event.target).checked)">
              <label class="form-check-label phase-switch-label" [for]="namePrefix() + 'Phase'">
                Require full payment now
              </label>
            </div>
          }
          @if (phaseExplanation(); as explain) {
            <p class="phase-explain" [class.on]="bFullPaymentRequired() === true">
              <i class="bi {{ phaseIcon() }}"></i><span>{{ explain }}</span>
            </p>
          }
          @if (phaseNote()) {
            <p class="phase-note">{{ phaseNote() }}</p>
          }
        </div>
      }

      @for (mod of modifiers(); track mod.modifierType) {
        @if ($first) {
          <div class="modifier-labels">
            <span class="mod-label mod-label-type">Type</span>
            <span class="mod-label mod-label-amount">Amount</span>
            <span class="mod-label-spacer"></span>
          </div>
        }
        <div class="modifier-row">
          <span class="mod-type-label"
                [class.mod-type-earlybird]="mod.modifierType !== 'LateFee'"
                [class.mod-type-latefee]="mod.modifierType === 'LateFee'">
            {{ modLabel(mod.modifierType) }}
          </span>
          <div class="input-group input-group-sm mod-amount">
            <span class="input-group-text">$</span>
            <input class="form-control" type="number" step="1"
                   [(ngModel)]="mod.amount" [name]="namePrefix() + 'ModAmt' + $index">
          </div>
          <button type="button" class="btn btn-sm btn-outline-danger btn-icon"
                  (click)="removeModifier($index)">
            <i class="bi bi-x"></i>
          </button>
          <div class="mod-dates">
            @if ($index === 0) {
              <div class="mod-date-field">
                <span class="mod-label">Start Date</span>
                <input class="form-control form-control-sm" type="date"
                       [(ngModel)]="mod.startDate" [name]="namePrefix() + 'ModStart' + $index">
              </div>
              <div class="mod-date-field">
                <span class="mod-label">End Date</span>
                <input class="form-control form-control-sm" type="date"
                       [(ngModel)]="mod.endDate" [name]="namePrefix() + 'ModEnd' + $index">
              </div>
            } @else {
              <input class="form-control form-control-sm" type="date"
                     [(ngModel)]="mod.startDate" [name]="namePrefix() + 'ModStart' + $index">
              <input class="form-control form-control-sm" type="date"
                     [(ngModel)]="mod.endDate" [name]="namePrefix() + 'ModEnd' + $index">
            }
          </div>
        </div>
      }

      <div class="d-flex flex-wrap gap-3 mt-1">
        <button type="button" class="btn btn-sm btn-link text-body-secondary p-0"
                [disabled]="hasModifier('EarlyBird')"
                (click)="addModifier('EarlyBird')">
          <i class="bi bi-plus-circle me-1"></i>Add Early Bird Discount
        </button>
        <button type="button" class="btn btn-sm btn-link text-body-secondary p-0"
                [disabled]="hasModifier('LateFee')"
                (click)="addModifier('LateFee')">
          <i class="bi bi-plus-circle me-1"></i>Add Late Fee
        </button>
      </div>

      @if (hasWindowOverlap()) {
        <div class="overlap-warning">
          <i class="bi bi-exclamation-triangle-fill me-1"></i>
          Early Bird and Late Fee date windows overlap — a registrant in the overlap
          would receive both. End the Early Bird before the Late Fee begins.
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .section-card {
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
      padding: var(--space-3);
      margin-bottom: var(--space-3);
    }
    .section-card-header {
      font-size: var(--font-size-xs); font-weight: 700; text-transform: uppercase;
      letter-spacing: 0.04em; color: var(--bs-secondary-color);
      margin-bottom: var(--space-2); display: flex; align-items: center; gap: var(--space-1);
    }

    .fee-card-player {
      border-left: 3px solid var(--bs-info);
      background: rgba(var(--bs-info-rgb), 0.04);
      box-shadow: var(--shadow-sm);
    }
    .fee-card-player .section-card-header { color: var(--bs-info); }

    .fee-card-clubrep {
      border-left: 3px solid var(--bs-warning);
      background: rgba(var(--bs-warning-rgb), 0.04);
      box-shadow: var(--shadow-sm);
    }
    .fee-card-clubrep .section-card-header { color: var(--bs-warning); }

    .fee-row { display: flex; gap: var(--space-2); }
    .fee-field { flex: 1; }
    .fee-label { font-size: var(--font-size-xs); color: var(--bs-secondary-color); margin-bottom: 2px; display: block; }
    .fee-hint { font-size: var(--font-size-xs); color: var(--bs-secondary-color); margin: 0 0 var(--space-2) 0; font-style: italic; }

    .phase-section {
      display: flex; flex-direction: column; gap: var(--space-2);
      margin: var(--space-3) 0;
      padding: var(--space-2) var(--space-3);
      background: var(--bs-tertiary-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
    }
    .phase-section-label {
      font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; margin-bottom: 0;
    }
    .phase-switch-label { font-size: var(--font-size-xs); font-weight: 600; cursor: pointer; }
    .phase-explain {
      font-size: var(--font-size-xs); color: var(--bs-secondary-color); margin: 0;
      display: flex; align-items: flex-start; gap: 4px;
    }
    .phase-explain.on { color: var(--bs-success); font-weight: 600; }
    .phase-explain i { margin-top: 1px; }
    .phase-note { font-size: var(--font-size-xs); color: var(--bs-secondary-color); margin: 0; display: flex; align-items: center; }

    .modifier-labels {
      display: grid;
      grid-template-columns: 1fr 100px auto;
      gap: var(--space-1);
      margin-top: var(--space-3);
    }
    .mod-label {
      font-size: var(--font-size-xs);
      color: var(--bs-secondary-color);
      margin-bottom: 2px;
      display: block;
    }
    .mod-label-spacer { width: 24px; }
    .modifier-row {
      display: grid;
      grid-template-columns: 1fr 100px auto;
      gap: var(--space-1);
      margin-top: var(--space-1);
      align-items: center;
    }
    .modifier-row:first-of-type { margin-top: 0; }
    .mod-type-label {
      min-width: 0;
      display: flex;
      align-items: center;
      font-size: var(--font-size-xs);
      font-weight: 600;
      padding: 0 var(--space-2);
      height: calc(1.5em + 0.5rem + 2px); /* matches form-control-sm height */
      border-radius: var(--radius-sm);
      border: 1px solid var(--bs-border-color);
      background: var(--bs-tertiary-bg);
    }
    .mod-type-earlybird { color: var(--bs-success); border-left: 3px solid var(--bs-success); }
    .mod-type-latefee { color: var(--bs-danger); border-left: 3px solid var(--bs-danger); }
    .mod-amount { min-width: 0; }
    .overlap-warning {
      margin-top: var(--space-2);
      padding: var(--space-2);
      font-size: var(--font-size-xs);
      color: var(--bs-warning-text-emphasis);
      background: var(--bs-warning-bg-subtle);
      border: 1px solid var(--bs-warning-border-subtle);
      border-radius: var(--radius-sm);
    }
    .mod-dates {
      grid-column: 1 / -1;
      display: flex;
      gap: var(--space-1);
      margin-top: var(--space-2);
    }
    .mod-dates input, .mod-date-field { flex: 1; min-width: 0; }
    .btn-icon { padding: 0.15rem 0.35rem; line-height: 1; }
  `]
})
export class FeeCardComponent {
  readonly header = input.required<string>();
  readonly headerIcon = input.required<string>();
  readonly variant = input.required<'player' | 'clubrep'>();
  readonly namePrefix = input.required<string>();
  readonly deposit = input<number | null>(null);
  readonly balanceDue = input<number | null>(null);
  readonly modifiers = input<ModifierForm[]>([]);
  // TODO: Skipped for migration because:
  //  This input is used in a control flow expression (e.g. `@if` or `*ngIf`)
  //  and migrating would break narrowing currently.
  @Input() hintText: string | null = null;
  readonly placeholder = input('');
  /** When false, hides the Deposit/Balance Due fields (e.g. league scope = modifiers only). */
  readonly showBaseFee = input(true);

  /**
   * Per-scope full-payment phase override: true = on, null = inherit from a less-specific
   * scope / the job baseline. v1 is two-state (the switch writes true or null).
   */
  readonly bFullPaymentRequired = input<boolean | null>(null);

  /** Optional read-only phase status line — used at league scope when no fee is set here,
   *  to point the admin to where phase is managed (age group / team). */
  readonly phaseNote = input<string | null>(null);

  /** Cascade scope — names who this card's setting flows down to (and who can override it)
   *  in the phase explanation copy. */
  readonly scope = input<'league' | 'agegroup' | 'team' | null>(null);

  readonly depositChange = output<number | null>();
  readonly balanceDueChange = output<number | null>();
  readonly bFullPaymentRequiredChange = output<boolean | null>();

  /**
   * The phase toggle only does something when there's a deposit to defer. With a
   * balance-only fee, ON and OFF collect the same amount (the balance), so the toggle is a
   * no-op — we hide it and show a single-payment line instead.
   */
  readonly showPhaseToggle = computed(() => (this.deposit() ?? 0) > 0);

  /**
   * Director-facing explanation of the effective phase — amount-aware and scope-aware.
   * Balance-only → single payment; deposit + balance → ON = full now (+ cascade down),
   * OFF = inherit (+ a more-specific scope can still require full payment).
   */
  readonly phaseExplanation = computed<string | null>(() => {
    const d = this.deposit() ?? 0;
    const b = this.balanceDue() ?? 0;
    if (d <= 0) {
      return b > 0 ? `Single payment of ${this.money(b)} at registration — no deposit to defer.` : null;
    }
    if (this.bFullPaymentRequired() === true) {
      return `Full payment now: collect ${this.money(d)} + ${this.money(b)} = ${this.money(d + b)} `
           + `at registration — no Final Balance Due.${this.cascadeOn()}`;
    }
    return `Inheriting the payment phase. Unless a higher level requires full payment, registrants pay `
         + `${this.money(d)} now and ${this.money(b)} later (Final Balance Due).${this.cascadeOff()}`;
  });

  readonly phaseIcon = computed(() => {
    if ((this.deposit() ?? 0) <= 0) return 'bi-cash';
    return this.bFullPaymentRequired() === true ? 'bi-cash-stack' : 'bi-hourglass-split';
  });

  onPhaseToggle(checked: boolean): void {
    this.bFullPaymentRequiredChange.emit(checked ? true : null);
  }

  private money(n: number): string {
    return '$' + Math.round(n).toLocaleString('en-US');
  }

  /** Downward reach when full payment is required at this scope. */
  private cascadeOn(): string {
    switch (this.scope()) {
      case 'league': return ' Applies to every age group and team in this league.';
      case 'agegroup': return ' Applies to every team in this age group.';
      case 'team': return ' Applies to this team.';
      default: return '';
    }
  }

  /** Override note when this scope is inheriting (a more-specific scope can still escalate). */
  private cascadeOff(): string {
    switch (this.scope()) {
      case 'league': return ' An age group or team can still require full payment on its own.';
      case 'agegroup': return ' A team can still require full payment on its own.';
      default: return '';
    }
  }

  modLabel(type: string): string {
    return type === 'LateFee' ? 'Late Fee' : 'Early Bird Discount';
  }

  hasModifier(type: 'EarlyBird' | 'LateFee'): boolean {
    return this.modifiers().some(m => m.modifierType === type);
  }

  addModifier(type: 'EarlyBird' | 'LateFee'): void {
    if (this.hasModifier(type)) return;   // max one of each type
    this.modifiers().push({ modifierType: type, amount: null, startDate: null, endDate: null });
  }

  /** True when an Early Bird and a Late Fee on this card have overlapping date windows. */
  hasWindowOverlap(): boolean {
    const ebs = this.modifiers().filter(m => m.modifierType === 'EarlyBird');
    const lfs = this.modifiers().filter(m => m.modifierType === 'LateFee');
    for (const a of ebs) {
      for (const b of lfs) {
        // null start = open past, null end = open future; boundaries inclusive.
        const s1 = a.startDate ? new Date(a.startDate).getTime() : -Infinity;
        const e1 = a.endDate ? new Date(a.endDate).getTime() : Infinity;
        const s2 = b.startDate ? new Date(b.startDate).getTime() : -Infinity;
        const e2 = b.endDate ? new Date(b.endDate).getTime() : Infinity;
        if (s1 <= e2 && s2 <= e1) return true;
      }
    }
    return false;
  }

  removeModifier(index: number): void {
    this.modifiers().splice(index, 1);
  }
}
