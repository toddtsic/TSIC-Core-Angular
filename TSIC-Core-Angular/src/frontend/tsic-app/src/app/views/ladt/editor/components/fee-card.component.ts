import { ChangeDetectionStrategy, Component, Input, computed, input, output } from '@angular/core';
import { ControlContainer, FormsModule, NgForm } from '@angular/forms';

export interface ModifierForm {
  feeModifierId?: string | null;
  modifierType: string;
  amount: number | null;
  startDate: string | null;
  endDate: string | null;
}

/**
 * The phase resolved from the tiers ABOVE a card's own scope (what governs when the
 * "Use league/age group setting" radio is chosen): the effective value plus the tier
 * that set it ('league' / 'agegroup', or 'job' = nothing set anywhere → deposit).
 */
export interface AncestorPhase {
  full: boolean;
  source: string;
}

/**
 * Phase-relevance context for a card's scope, computed by the parent from the cached job
 * fee rows. `depositInScope` = a deposit exists at this scope, at a governing tier above,
 * or (league/agegroup) at any tier below — i.e. the payment phase can actually engage
 * somewhere this card's setting reaches. `resolvedDeposit`/`resolvedBalance` = the
 * cascade-resolved amounts for this scope (own row ?? governing tiers), used to quote the
 * single-payment note when the local inputs are blank.
 */
export interface PhaseContext {
  depositInScope: boolean;
  resolvedDeposit: number | null;
  resolvedBalance: number | null;
}

/**
 * Reverse-cascade disclosure: one scope ONE TIER BELOW this card (an age group under the
 * league card, a team under an age-group card) that sets its own value for something this
 * card's scope would otherwise govern. Field-aware by design — a row's mere existence is
 * NOT an override (player rows exist structurally at age-group scope in every job); only
 * a locally-set phase stamp, amount, or modifier counts.
 */
export interface DescendantOverrideInfo {
  name: string;
  /** Own phase stamp: true = Full payment, false = Deposit veto. null = no stamp here. */
  phase: boolean | null;
  /** Own deposit amount. null = not set at this scope (inherits). */
  deposit: number | null;
  /** Own balance-due amount. null = not set at this scope (inherits). */
  balanceDue: number | null;
  /** Sets its own Early Bird Discount. */
  earlyBird: boolean;
  /** Sets its own Late Fee. */
  lateFee: boolean;
}

/**
 * Every Early Bird Discount and Late Fee must carry BOTH a start AND an end date — the window
 * is what gives the modifier meaning. An open-ended late fee silently behaves as a permanent
 * surcharge "active since the dawn of time"; an open-ended early bird is a permanent discount.
 * Amount-less rows are ignored (they are dropped on save). Returns an error message naming the
 * offending modifier, or null when valid. Mirrors the backend FeeController guard.
 */
export function modifierDateError(mods: ModifierForm[]): string | null {
  for (const m of mods) {
    if (m.amount == null || m.amount <= 0) continue;
    if (!m.startDate || !m.endDate) {
      const label = m.modifierType === 'LateFee' ? 'Late Fee' : 'Early Bird Discount';
      return `${label} requires both a start date and an end date.`;
    }
  }
  return null;
}

@Component({
  selector: 'app-fee-card',
  standalone: true,
  imports: [FormsModule],
  // Make this card's ngModel controls (deposit, balance, modifier amounts/dates) register
  // with the host detail panel's <form>, so editing a fee dirties that form and the
  // unsaved-changes guard fires. Without this, ngModel's @Host() lookup stops at this
  // component and the controls stay standalone (silent data loss on close). The phase
  // toggle has no control, so panels mark dirty on its change separately.
  viewProviders: [{ provide: ControlContainer, useExisting: NgForm }],
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
        <div class="fee-row" [class.fee-inputs-locked]="amountsDisabled()">
          <div class="fee-field">
            <label class="fee-label">Deposit</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="deposit()" (ngModelChange)="depositChange.emit($event)"
                     [name]="namePrefix() + 'Deposit'"
                     [placeholder]="placeholder()"
                     [attr.disabled]="amountsDisabled() ? '' : null"
                     (blur)="amountCommitted.emit()">
            </div>
          </div>
          <div class="fee-field">
            <label class="fee-label">Balance Due</label>
            <div class="input-group input-group-sm">
              <span class="input-group-text">$</span>
              <input class="form-control" type="number" step="1"
                     [ngModel]="balanceDue()" (ngModelChange)="balanceDueChange.emit($event)"
                     [name]="namePrefix() + 'BalanceDue'"
                     [placeholder]="placeholder()"
                     [attr.disabled]="amountsDisabled() ? '' : null"
                     (blur)="amountCommitted.emit()">
            </div>
          </div>
        </div>

        @if (amountOverrideNote(); as note) {
          <p class="cascade-below-note"><i class="bi bi-arrow-down-circle"></i><span>{{ note }}</span></p>
        }
      }

      @if (showPhaseBlock()) {
      <div class="phase-section" [class.fee-inputs-locked]="toggleDisabled()">
        <label class="fee-label phase-section-label">Payment Phase</label>
        <!-- Radio group, one choice per stored value. The "use the level above" option is the
             blank-field equivalent of the Deposit/Balance inputs and names its source plainly
             (like the amount placeholders) — no jargon. -->
        <div class="phase-radios" role="radiogroup" [attr.aria-label]="'Payment phase — ' + header()">
          @if (scope() !== 'league') {
            <div class="form-check">
              <input class="form-check-input" type="radio"
                     [name]="namePrefix() + 'PhaseRb'" [id]="namePrefix() + 'PhaseDefault'"
                     [checked]="phaseChoice() === 'inherit'" [disabled]="toggleDisabled()"
                     (change)="onPhaseSelect(null)">
              <label class="form-check-label" [for]="namePrefix() + 'PhaseDefault'">{{ defaultOptionLabel() }}</label>
            </div>
          }
          <div class="form-check">
            <input class="form-check-input" type="radio"
                   [name]="namePrefix() + 'PhaseRb'" [id]="namePrefix() + 'PhaseDeposit'"
                   [checked]="phaseChoice() === 'deposit'" [disabled]="toggleDisabled()"
                   (change)="onPhaseSelect(scope() === 'league' ? null : false)">
            <label class="form-check-label" [for]="namePrefix() + 'PhaseDeposit'">Deposit first</label>
          </div>
          <div class="form-check">
            <input class="form-check-input" type="radio"
                   [name]="namePrefix() + 'PhaseRb'" [id]="namePrefix() + 'PhaseFull'"
                   [checked]="phaseChoice() === 'full'" [disabled]="toggleDisabled()"
                   (change)="onPhaseSelect(true)">
            <label class="form-check-label" [for]="namePrefix() + 'PhaseFull'">Full payment now</label>
          </div>
        </div>
        @if (phaseExplanation(); as explain) {
          <p class="phase-explain" [class.on]="explainOn()">
            <i class="bi {{ phaseIcon() }}"></i><span>{{ explain }}</span>
          </p>
        }
        <!-- Downward mirror of the "Currently: … set at league level" line: names the
             more-specific scopes that stamp their own phase, so this card never reads
             as governing everything when it doesn't. -->
        @if (phaseOverrideNote(); as note) {
          <p class="phase-explain cascade-below-note"><i class="bi bi-arrow-down-circle"></i><span>{{ note }}</span></p>
        }
      </div>
      } @else {
        <!-- No deposit anywhere this card's phase could reach and no stamp here: the phase
             cannot engage, so the radio group collapses to the truth of what gets charged.
             Typing a deposit (or a saved deposit/stamp appearing at a governing tier)
             brings the full control back. -->
        <p class="phase-explain single-payment-note">
          <i class="bi bi-cash"></i><span>{{ singlePaymentNote() }}</span>
        </p>
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

      @if (modifierOverrideNote(); as note) {
        <p class="cascade-below-note"><i class="bi bi-arrow-down-circle"></i><span>{{ note }}</span></p>
      }

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

    .fee-inputs-locked {
      opacity: 0.45;
      pointer-events: none;
    }

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
    .phase-radios {
      display: flex; flex-wrap: wrap;
      gap: var(--space-1) var(--space-4);
    }
    .phase-radios .form-check { margin-bottom: 0; }
    .phase-radios .form-check-label {
      font-size: var(--font-size-xs); font-weight: 600; cursor: pointer;
    }
    .phase-radios .form-check-input { cursor: pointer; }
    .phase-radios .form-check-input:focus-visible {
      outline: none;
      box-shadow: var(--shadow-focus);
    }
    .phase-explain {
      font-size: var(--font-size-xs); color: var(--bs-secondary-color); margin: 0;
      display: flex; align-items: flex-start; gap: 4px;
    }
    .phase-explain.on { color: var(--bs-success); font-weight: 600; }
    .phase-explain i { margin-top: 1px; }
    /* Downward mirror of the "follows the level above" line — names the more-specific
       scopes that set their own value. Same quiet voice as .phase-explain wherever it
       appears (under the amounts, in the phase block, after the modifier links). */
    .cascade-below-note {
      font-size: var(--font-size-xs); color: var(--bs-secondary-color);
      margin: var(--space-1) 0 0 0;
      display: flex; align-items: flex-start; gap: 4px;
    }
    .cascade-below-note i { margin-top: 1px; }
    /* Collapsed phase block — same quiet chrome as the radio container, one line tall. */
    .single-payment-note {
      margin: var(--space-3) 0;
      padding: var(--space-2) var(--space-3);
      background: var(--bs-tertiary-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
    }

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
   * Per-scope payment phase, cascading like every other fee field (most-specific non-null
   * wins): true = full payment, false = explicit deposit (a veto that stops the cascade),
   * null = inherit from a less-specific scope (deposit if no scope sets it). At league
   * scope — the top tier — false and null resolve identically, so the card exposes only
   * Deposit/Full there and "Deposit first" writes null (no pointless row).
   */
  readonly bFullPaymentRequired = input<boolean | null>(null);

  /** Resolved phase from the tiers above this scope — lets the "use the level above" choice
   *  state the actual outcome ("Currently: Full payment now — set at league level") instead
   *  of describing the cascade rule. Omitted at league scope (nothing above). */
  readonly ancestorPhase = input<AncestorPhase | null>(null);

  /** Phase-relevance for this card's scope (see PhaseContext). null = no context supplied —
   *  the phase control renders unconditionally (legacy behavior). */
  readonly phaseContext = input<PhaseContext | null>(null);

  /** Cascade scope — names who this card's setting flows down to (and who can override it)
   *  in the phase explanation copy. */
  readonly scope = input<'league' | 'agegroup' | 'team' | null>(null);

  /** Scopes one tier below this card with their OWN settings (league card = age groups,
   *  age-group card = teams) — drives the downward "set their own …" disclosure lines.
   *  null / empty = nothing overridden below (and always at team scope, the leaf). */
  readonly descendantOverrides = input<DescendantOverrideInfo[] | null>(null);

  readonly depositChange = output<number | null>();
  readonly balanceDueChange = output<number | null>();
  readonly bFullPaymentRequiredChange = output<boolean | null>();
  /** Fires when the user commits a fee amount change (blur on deposit or balance-due). */
  readonly amountCommitted = output<void>();
  /** When true, deposit and balance-due inputs are visually locked (fee-phase or settings edits in progress). */
  readonly amountsDisabled = input(false);
  /** When true, the payment-phase toggle is visually locked (fee-amount or settings edits in progress). */
  readonly toggleDisabled = input(false);

  /**
   * The stored phase value mapped to the radio group's selection. Below league,
   * null = "use the level above's setting" and false = an explicit Deposit veto. At
   * league — the top tier, with nothing above to follow — null (and a legacy false)
   * both read as Deposit.
   */
  /**
   * Phase control disclosure: the radio group only renders where the phase can matter.
   * Visible when a deposit exists anywhere in this card's reach (context), when THIS scope
   * carries a stamp (a stamp must never sit under a hidden control — it must stay visible
   * and clearable), or when a deposit is being typed locally right now (unsaved). Without a
   * context the card behaves as before — always show.
   */
  readonly showPhaseBlock = computed(() => {
    const ctx = this.phaseContext();
    if (!ctx) return true;
    return ctx.depositInScope
      || this.bFullPaymentRequired() !== null
      || (this.deposit() ?? 0) > 0;
  });

  /** The collapsed phase block's one-line truth: with no deposit configured anywhere, the
   *  full fee is collected at registration. Quotes the cascade-resolved amount below league
   *  scope (league amounts live at many tiers below — no single number to quote). */
  readonly singlePaymentNote = computed(() => {
    const who = this.variant() === 'player' ? 'players' : 'club reps';
    const bal = this.balanceDue() ?? this.phaseContext()?.resolvedBalance ?? 0;
    const charge = this.scope() !== 'league' && bal > 0 ? `${this.money(bal)} in full` : 'the full fee';
    return `Single payment: ${who} pay ${charge} at registration. Set a deposit to enable two-phase payment.`;
  });

  readonly phaseChoice = computed<'inherit' | 'deposit' | 'full'>(() => {
    const v = this.bFullPaymentRequired();
    if (v === true) return 'full';
    if (v === false) return 'deposit';
    return this.scope() === 'league' ? 'deposit' : 'inherit';
  });

  /**
   * Director-facing explanation of the selected phase — amount-aware (quotes this card's
   * deposit/balance when both are set; a league/age-group stamp also governs lower scopes
   * whose amounts live elsewhere, so no local amounts ≠ no effect) and scope-aware
   * (cascade reach + who can still override).
   */
  readonly phaseExplanation = computed<string | null>(() => {
    const d = this.deposit() ?? 0;
    const b = this.balanceDue() ?? 0;
    // Leaf scope with a single local payment — the phase genuinely cannot matter here.
    if (this.scope() === 'team' && d <= 0 && b > 0) {
      return `Single payment of ${this.money(b)} at registration — no deposit to defer.`;
    }
    switch (this.phaseChoice()) {
      case 'full': {
        const amounts = d > 0 && b > 0
          ? `: collect ${this.money(d)} + ${this.money(b)} = ${this.money(d + b)} at registration — no Final Balance Due`
          : ' — the full price is collected at registration';
        return `Full payment${amounts}.${this.cascadeOn()}`;
      }
      case 'deposit': {
        const amounts = d > 0 && b > 0 ? ` (${this.money(d)} now, ${this.money(b)} later)` : '';
        return this.scope() === 'league'
          ? `Deposit first${amounts}: registrants pay the deposit at registration and the Final Balance Due later.${this.cascadeOff()}`
          : `Deposit first${amounts}, regardless of any higher-level setting.${this.cascadeOn()}`;
      }
      default: {
        // Prefer the resolved verdict: what this scope is actually in, and who decided.
        const a = this.ancestorPhase();
        if (a) {
          return `Currently: ${a.full ? 'Full payment now' : 'Deposit first'} — ${this.phaseSourceLabel(a.source)}.`;
        }
        const from = this.scope() === 'team' ? 'the age group or league' : 'the league';
        return `No phase set here — follows ${from}; deposit first when nothing is set anywhere.${this.cascadeOff()}`;
      }
    }
  });

  readonly phaseIcon = computed(() => {
    if (this.scope() === 'team' && (this.deposit() ?? 0) <= 0 && (this.balanceDue() ?? 0) > 0) return 'bi-cash';
    switch (this.phaseChoice()) {
      case 'full': return 'bi-cash-stack';
      case 'deposit': return 'bi-hourglass-split';
      default: return 'bi-arrow-up-circle';
    }
  });

  /** Plain-language label for the "no setting here" radio — names where the phase comes from,
   *  the way the amount placeholders name their default source. */
  defaultOptionLabel(): string {
    return this.scope() === 'team' ? 'Use age group setting' : 'Use league setting';
  }

  /** Names the tier that decided the resolved phase for the "Currently: …" line. */
  private phaseSourceLabel(source: string): string {
    switch (source) {
      case 'league': return 'set at league level';
      case 'agegroup': return 'set at age group level';
      default: return 'the default; no level sets a phase';
    }
  }

  /** Cap on names enumerated in a "set their own …" line before collapsing to "and N more". */
  private static readonly BELOW_NOTE_MAX = 5;

  /** "1 age group sets its" / "3 teams set their" — the subject of every below-note. */
  private countPhrase(n: number): string {
    const noun = this.scope() === 'agegroup' ? 'team' : 'age group';
    return n === 1 ? `1 ${noun} sets its` : `${n} ${noun}s set their`;
  }

  /**
   * Downward phase disclosure: names each more-specific scope that stamps its own phase,
   * with its value — without this, a league card reading "applies to every age group…"
   * looks universal even when an age group has already gone its own way.
   */
  readonly phaseOverrideNote = computed<string | null>(() => {
    const below = (this.descendantOverrides() ?? []).filter(d => d.phase !== null);
    if (!below.length) return null;
    const shown = below.slice(0, FeeCardComponent.BELOW_NOTE_MAX);
    const names = shown.map(d => `${d.name} (${d.phase ? 'Full payment' : 'Deposit first'})`).join(', ');
    const more = below.length > shown.length ? ` and ${below.length - shown.length} more` : '';
    return `${this.countPhrase(below.length)} own phase: ${names}${more}.`;
  });

  /**
   * Downward amounts disclosure. Enumerates with amounts when short; past the cap it
   * collapses to a count — the sibling grid alongside already itemizes every row, so the
   * card only needs to say the league/age-group inputs are not the whole story.
   */
  readonly amountOverrideNote = computed<string | null>(() => {
    const below = (this.descendantOverrides() ?? []).filter(d => d.deposit != null || d.balanceDue != null);
    if (!below.length) return null;
    if (below.length > FeeCardComponent.BELOW_NOTE_MAX) {
      return `${this.countPhrase(below.length)} own amounts — see each row in the grid.`;
    }
    const names = below.map(d => `${d.name} (${this.amountsLabel(d)})`).join(', ');
    return `${this.countPhrase(below.length)} own amounts: ${names}.`;
  });

  private amountsLabel(d: DescendantOverrideInfo): string {
    if (d.deposit != null && d.balanceDue != null) return `${this.money(d.deposit)} + ${this.money(d.balanceDue)}`;
    if (d.deposit != null) return `${this.money(d.deposit)} deposit`;
    return `${this.money(d.balanceDue!)} balance`;
  }

  /** Downward Early Bird / Late Fee disclosure — same voice as the other below-notes. */
  readonly modifierOverrideNote = computed<string | null>(() => {
    const below = (this.descendantOverrides() ?? []).filter(d => d.earlyBird || d.lateFee);
    if (!below.length) return null;
    const label = (d: DescendantOverrideInfo) =>
      [d.earlyBird ? 'Early Bird' : null, d.lateFee ? 'Late Fee' : null].filter(Boolean).join(' + ');
    const shown = below.slice(0, FeeCardComponent.BELOW_NOTE_MAX);
    const names = shown.map(d => `${d.name} (${label(d)})`).join(', ');
    const more = below.length > shown.length ? ` and ${below.length - shown.length} more` : '';
    return `${this.countPhrase(below.length)} own Early Bird / Late Fee: ${names}${more}.`;
  });

  /** Green "you're in full-payment" emphasis — own stamp OR the resolved answer when following
   *  the level above (the old own-stamp-only check left an effectively-full card looking idle). */
  readonly explainOn = computed(() =>
    this.bFullPaymentRequired() === true
    || (this.phaseChoice() === 'inherit' && this.ancestorPhase()?.full === true));

  /** Re-selecting the current choice is a no-op (don't dirty the form or re-open prompts). */
  onPhaseSelect(value: boolean | null): void {
    if (value === this.bFullPaymentRequired()) return;
    this.bFullPaymentRequiredChange.emit(value);
  }

  private money(n: number): string {
    return '$' + Math.round(n).toLocaleString('en-US');
  }

  /** Downward reach of an explicit phase at this scope (a lower scope's own stamp still wins). */
  private cascadeOn(): string {
    switch (this.scope()) {
      case 'league': return ' Applies to every age group and team that doesn\'t set its own phase.';
      case 'agegroup': return ' Applies to every team in this age group that doesn\'t set its own phase.';
      case 'team': return ' Applies to this team only.';
      default: return '';
    }
  }

  /** Override note when this scope sets nothing binding downward (inherit / league default). */
  private cascadeOff(): string {
    switch (this.scope()) {
      case 'league': return ' An age group or team can still set its own phase.';
      case 'agegroup': return ' A team can still set its own phase.';
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
