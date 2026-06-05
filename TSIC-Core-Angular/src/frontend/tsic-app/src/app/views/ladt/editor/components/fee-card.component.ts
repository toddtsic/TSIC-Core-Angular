import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter } from '@angular/core';
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
         [class.fee-card-player]="variant === 'player'"
         [class.fee-card-clubrep]="variant === 'clubrep'">
      <div class="section-card-header">
        <i class="bi {{ headerIcon }}"></i> {{ header }}
      </div>

      @if (hintText) {
        <p class="fee-hint">{{ hintText }}</p>
      }

      @if (showBaseFee) {
        <div class="fee-row">
          <div class="fee-field">
            <label class="fee-label">Deposit</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="deposit" (ngModelChange)="depositChange.emit($event)"
                     [name]="namePrefix + 'Deposit'"
                     [placeholder]="placeholder">
            </div>
          </div>
          <div class="fee-field">
            <label class="fee-label">Balance Due</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="balanceDue" (ngModelChange)="balanceDueChange.emit($event)"
                     [name]="namePrefix + 'BalanceDue'"
                     [placeholder]="placeholder">
            </div>
          </div>
        </div>
      }

      @for (mod of modifiers; track mod.modifierType) {
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
                   [(ngModel)]="mod.amount" [name]="namePrefix + 'ModAmt' + $index">
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
                       [(ngModel)]="mod.startDate" [name]="namePrefix + 'ModStart' + $index">
              </div>
              <div class="mod-date-field">
                <span class="mod-label">End Date</span>
                <input class="form-control form-control-sm" type="date"
                       [(ngModel)]="mod.endDate" [name]="namePrefix + 'ModEnd' + $index">
              </div>
            } @else {
              <input class="form-control form-control-sm" type="date"
                     [(ngModel)]="mod.startDate" [name]="namePrefix + 'ModStart' + $index">
              <input class="form-control form-control-sm" type="date"
                     [(ngModel)]="mod.endDate" [name]="namePrefix + 'ModEnd' + $index">
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
  @Input({ required: true }) header!: string;
  @Input({ required: true }) headerIcon!: string;
  @Input({ required: true }) variant!: 'player' | 'clubrep';
  @Input({ required: true }) namePrefix!: string;
  @Input() deposit: number | null = null;
  @Input() balanceDue: number | null = null;
  @Input() modifiers: ModifierForm[] = [];
  @Input() hintText: string | null = null;
  @Input() placeholder = '';
  /** When false, hides the Deposit/Balance Due fields (e.g. league scope = modifiers only). */
  @Input() showBaseFee = true;

  @Output() depositChange = new EventEmitter<number | null>();
  @Output() balanceDueChange = new EventEmitter<number | null>();

  modLabel(type: string): string {
    return type === 'LateFee' ? 'Late Fee' : 'Early Bird Discount';
  }

  hasModifier(type: 'EarlyBird' | 'LateFee'): boolean {
    return this.modifiers.some(m => m.modifierType === type);
  }

  addModifier(type: 'EarlyBird' | 'LateFee'): void {
    if (this.hasModifier(type)) return;   // max one of each type
    this.modifiers.push({ modifierType: type, amount: null, startDate: null, endDate: null });
  }

  removeModifier(index: number): void {
    this.modifiers.splice(index, 1);
  }
}
