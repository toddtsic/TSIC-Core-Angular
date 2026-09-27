import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import type { AgeGroupDto } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { formatLop } from '@shared/teams/lop-choices';
import { describeSlotPricing, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, isWaitlistAgeGroup, type LibraryRegisterRequest } from './library-segment.types';

/** One line of the dialog: a candidate plus what registering it would do, given the ticks above it. */
interface RegisterAllLine {
    req: LibraryRegisterRequest;
    checked: boolean;
    ageGroupLabel: string;
    waitlist: boolean;
    pricing: SlotPricing;
}

/**
 * "Register all N" — the moment the Club Team Library proves itself: last season's teams, into this
 * event, in one press. Every candidate arrives with its age group and level of play already
 * resolved (the segment only offers rows whose answer is obvious); the rep unticks any team they
 * are not bringing.
 *
 * Capacity is SIMULATED in list order over the ticked rows, so two 2030 teams heading for an age
 * group with one spot left read "1 spot, 1 waitlist" before the press, not after. The server still
 * decides on submit — this only keeps the dialog honest about what it is likely to do.
 */
@Component({
    selector: 'app-register-all-dialog',
    standalone: true,
    imports: [TsicDialogComponent, CurrencyPipe],
    template: `
    <tsic-dialog [open]="true" size="lg" (requestClose)="cancelled.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi bi-collection-fill me-2" aria-hidden="true"></i>Register from your Club Team Library
          </h5>
          <button type="button" class="btn-close" aria-label="Close" (click)="cancelled.emit()"></button>
        </div>

        <div class="modal-body">
          <p class="ra-lede">
            These teams are in your Club Team Library and each fits an age group at
            <strong>{{ eventName() }}</strong>. Untick any team you are not bringing.
          </p>

          <ul class="ra-list">
            @for (line of lines(); track line.req.team.clubTeamId) {
              <li>
                <label class="ra-row" [class.is-off]="!line.checked">
                  <input type="checkbox" class="form-check-input ra-check"
                         [checked]="line.checked"
                         (change)="toggle(line.req.team.clubTeamId)" />
                  <span class="ra-team">
                    <span class="ra-name">{{ line.req.team.clubTeamName }}</span>
                    <span class="ra-meta">
                      <span class="ra-key">Grad</span>{{ line.req.team.clubTeamGradYear || '—' }}
                      <span class="ra-key">LOP</span>{{ formatLop(line.req.levelOfPlay) }}
                    </span>
                  </span>
                  <span class="ra-into">
                    @if (line.waitlist) {
                      <span class="ra-wl"><i class="bi bi-hourglass-split" aria-hidden="true"></i>{{ line.ageGroupLabel }} waitlist</span>
                    } @else {
                      <span class="ra-ag"><i class="bi bi-arrow-right-short" aria-hidden="true"></i>{{ line.ageGroupLabel }}</span>
                    }
                  </span>
                  <span class="ra-fee">
                    @switch (line.pricing.kind) {
                      @case ('waitlist') { No fees while on waitlist }
                      @case ('free') { No fee }
                      @case ('deposit') { Deposit {{ $any(line.pricing).now | currency }} <small>of {{ $any(line.pricing).total | currency }}</small> }
                      @case ('full') { {{ $any(line.pricing).total | currency }} }
                    }
                  </span>
                </label>
              </li>
            }
          </ul>
        </div>

        <div class="modal-footer ra-footer">
          <span class="ra-total">
            @if (checkedCount() === 0) {
              No teams ticked
            } @else {
              <strong>{{ checkedCount() }} {{ checkedCount() === 1 ? 'team' : 'teams' }}</strong>
              &middot; {{ dueNow() | currency }} due now
              @if (dueLater() > 0) { &middot; {{ dueLater() | currency }} later }
              @if (waitlistCount() > 0) { &middot; {{ waitlistCount() }} to the waitlist }
            }
          </span>
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="cancelled.emit()">Cancel</button>
          <button type="button" class="btn btn-success btn-sm fw-semibold"
                  [disabled]="checkedCount() === 0"
                  (click)="confirm()">
            <i class="bi bi-trophy-fill me-1" aria-hidden="true"></i>
            Register {{ checkedCount() }} {{ checkedCount() === 1 ? 'team' : 'teams' }}
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
      .ra-lede {
        margin: 0 0 var(--space-3);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
      }

      .ra-list {
        list-style: none;
        margin: 0;
        padding: 0;
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        max-height: 55vh;
        overflow-y: auto;
      }

      .ra-list li + li { border-top: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent); }

      .ra-row {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr) auto 150px;
        align-items: center;
        gap: var(--space-3);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        cursor: pointer;
        transition: background-color 0.1s ease, opacity 0.1s ease;

        &:hover { background: color-mix(in srgb, var(--bs-body-color) 3%, transparent); }
        &.is-off { opacity: 0.5; }
      }

      .ra-check {
        margin: 0;
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .ra-team { display: flex; flex-direction: column; min-width: 0; }

      .ra-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .ra-meta {
        display: inline-flex;
        gap: var(--space-1);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .ra-key {
        text-transform: uppercase;
        letter-spacing: 0.06em;
        font-weight: var(--font-weight-semibold);
        opacity: 0.7;
      }
      .ra-meta .ra-key:not(:first-child) { margin-left: var(--space-2); }

      .ra-into { font-size: var(--font-size-sm); font-weight: var(--font-weight-semibold); white-space: nowrap; }
      .ra-ag { color: var(--bs-success); display: inline-flex; align-items: center; }
      .ra-wl {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--bs-warning-text-emphasis, var(--brand-text));
      }

      .ra-fee {
        text-align: right;
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        font-variant-numeric: tabular-nums;
        small { color: var(--brand-text-muted); }
      }

      .ra-footer {
        display: flex;
        align-items: center;
        gap: var(--space-2);
      }

      .ra-total {
        margin-right: auto;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        strong { color: var(--brand-text); }
      }

      @media (max-width: 575.98px) {
        .ra-row {
          grid-template-columns: auto minmax(0, 1fr) auto;
          grid-template-areas: 'check team into' 'check fee fee';
          row-gap: 2px;
        }
        .ra-check { grid-area: check; }
        .ra-team { grid-area: team; }
        .ra-into { grid-area: into; }
        .ra-fee { grid-area: fee; text-align: left; }
        .ra-footer { flex-wrap: wrap; }
        .ra-total { flex-basis: 100%; }
      }

      @media (prefers-reduced-motion: reduce) {
        .ra-row { transition: none !important; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterAllDialogComponent {
    readonly candidates = input.required<readonly LibraryRegisterRequest[]>();
    readonly ageGroups = input.required<readonly AgeGroupDto[]>();
    readonly eventName = input('this event');

    readonly confirmed = output<LibraryRegisterRequest[]>();
    readonly cancelled = output<void>();

    readonly formatLop = formatLop;

    /** Unticked clubTeamIds. Everything starts ticked: the list is already "the ones that fit". */
    private readonly unticked = signal<ReadonlySet<number>>(new Set());

    readonly lines = computed<RegisterAllLine[]>(() => {
        const off = this.unticked();
        const byId = new Map(this.ageGroups().map(a => [a.ageGroupId, a]));
        const spots = new Map(this.ageGroups().map(a => [a.ageGroupId, Math.max(0, a.maxTeams - a.registeredCount)]));

        return this.candidates().map(req => {
            const ag = byId.get(req.ageGroupId);
            const checked = !off.has(req.team.clubTeamId);
            if (!ag) {
                return { req, checked, ageGroupLabel: '', waitlist: false, pricing: { kind: 'free' } as SlotPricing };
            }
            let waitlist = isWaitlistAgeGroup(ag);
            if (!waitlist) {
                const left = spots.get(ag.ageGroupId) ?? 0;
                if (left <= 0) waitlist = true;
                else if (checked) spots.set(ag.ageGroupId, left - 1);
            }
            const pricing = waitlist
                ? { kind: 'waitlist' } as SlotPricing
                : describeSlotPricing({
                    isFull: false,
                    fee: (ag.deposit || 0) + (ag.balanceDue || 0),
                    deposit: ag.deposit || 0,
                    balanceDue: ag.balanceDue || 0,
                    fullPaymentRequired: !!ag.fullPaymentRequired,
                });
            return { req, checked, ageGroupLabel: ageGroupLabel(ag), waitlist, pricing };
        });
    });

    readonly checkedCount = computed(() => this.lines().filter(l => l.checked).length);
    readonly waitlistCount = computed(() => this.lines().filter(l => l.checked && l.waitlist).length);

    /** What the ticked teams add today — the deposit in a deposit-phase event, else the whole fee. */
    readonly dueNow = computed(() => this.lines()
        .filter(l => l.checked)
        .reduce((s, l) => s + (l.pricing.kind === 'deposit' ? l.pricing.now : l.pricing.kind === 'full' ? l.pricing.total : 0), 0));

    /** The balance slices of deposit-phase picks, billed when the director opens the final balance. */
    readonly dueLater = computed(() => this.lines()
        .filter(l => l.checked)
        .reduce((s, l) => s + (l.pricing.kind === 'deposit' ? l.pricing.total - l.pricing.now : 0), 0));

    toggle(clubTeamId: number): void {
        const next = new Set(this.unticked());
        if (next.has(clubTeamId)) next.delete(clubTeamId); else next.add(clubTeamId);
        this.unticked.set(next);
    }

    confirm(): void {
        const picked = this.lines().filter(l => l.checked).map(l => l.req);
        if (picked.length) this.confirmed.emit(picked);
    }
}
