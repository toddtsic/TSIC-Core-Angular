import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { RegisteredTeamDto } from '@core/api';

/**
 * Reusable registered-teams summary grid.
 * Used on the teams step (interactive, with delete), payment step (read-only), etc.
 *
 * Columns adapt based on input flags. Delete button shown when showRemove=true
 * and the team has paidTotal === 0. Parent handles removal via removeTeam output.
 */
@Component({
    selector: 'app-registered-teams-grid',
    standalone: true,
    imports: [CurrencyPipe, DatePipe, GridAllModule],
    template: `
      <ejs-grid #grid [dataSource]="teams()" [allowSorting]="true"
                [allowTextWrap]="true"
                [textWrapSettings]="{ wrapMode: 'Header' }"
                [height]="gridHeight()"
                [allowPaging]="pageSize() > 0"
                [pageSettings]="{ pageSize: pageSize() || 50 }"
                (dataBound)="onDataBound(grid)"
                (actionComplete)="onActionComplete($event, grid)"
                cssClass="tsic-grid-compact">
        <e-columns>
          <!-- Row number (unbound — stamped via refreshRowNumbers, survives sort) -->
          <e-column headerText="" width="40" textAlign="Center" [allowSorting]="false"
                    [isFrozen]="frozenTeamCol()"
                    [customAttributes]="{ class: 'row-number-cell' }"></e-column>
          <e-column field="teamName" [headerText]="teamColHeader()" [width]="teamColWidth()"
                    [isFrozen]="frozenTeamCol()"
                    [customAttributes]="{ class: 'team-name-wrap-cell' }">
            <ng-template #template let-data>
              <span class="team-name-cell">
                @if (showRemove() && data.paidTotal === 0) {
                  <button type="button" class="btn-inline-remove"
                          [disabled]="actionInProgress()"
                          (click)="removeTeam.emit(data)"
                          title="Remove {{ stripWaitlist(data.teamName) }} from event">
                    <i class="bi bi-trash3"></i>
                  </button>
                }
                <span class="fw-semibold">{{ stripWaitlist(data.teamName) }}</span>
              </span>
            </ng-template>
          </e-column>
          <e-column field="ageGroupName" headerText="Age Group" width="75" [visible]="showAgeGroup()">
            <ng-template #template let-data>
              <span class="agegroup-cell">
                {{ stripWaitlist(data.ageGroupName) }}
                @if (isWaitlisted(data.ageGroupName)) {
                  <span class="wl-badge" tabindex="0"
                        title="Waitlisted under {{ stripWaitlist(data.ageGroupName) }} — placed when a roster spot opens">WL</span>
                }
              </span>
            </ng-template>
          </e-column>
          <e-column field="levelOfPlay" headerText="LOP" width="55" textAlign="Center" [visible]="showLop()">
            <ng-template #template let-data>
              <span [attr.title]="data.levelOfPlay">{{ formatLop(data.levelOfPlay) }}</span>
            </ng-template>
          </e-column>
          <e-column field="registrationTs" headerText="Reg Date" width="80" type="date" format="MM/dd/yyyy"
                    [visible]="showRegDate()"></e-column>
          <e-column field="tenderPaid" headerText="Paid" width="75" textAlign="Right" format="C2"
                    [visible]="showPaid()">
            <ng-template #template let-data>
              <span [class.text-success]="data.tenderPaid > 0" [class.text-muted]="data.tenderPaid === 0">
                {{ data.tenderPaid | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="deposit" headerText="Deposit" width="85" textAlign="Right" format="C2"
                    [visible]="showStructure()"></e-column>
          <e-column field="balanceDue" headerText="Balance Due" width="100" textAlign="Right" format="C2"
                    [visible]="showStructure()"></e-column>
          <e-column field="depositDue" headerText="Deposit Due" width="75" textAlign="Right" format="C2"
                    [visible]="showDeposit()"></e-column>
          <e-column field="additionalDue" headerText="Balance Due" width="75" textAlign="Right" format="C2"
                    [visible]="showBalance()"></e-column>
          <!-- Total Fee = structural sum (Deposit + BalanceDue), not feeTotal which is
               phase-aware (deposit-phase total = deposit + processing). The field stays
               as 'feeTotal' so the aggregate footer aligns under this column; the cell
               template renders the structural sum instead of the bound value. -->
          <e-column field="feeTotal" headerText="Total Fee" width="80" textAlign="Right" [allowSorting]="false"
                    [visible]="showTotalFee()">
            <ng-template #template let-data>
              {{ (data.deposit + data.balanceDue) | currency }}
            </ng-template>
          </e-column>
          <e-column field="owedTotal" headerText="Owed" width="80" textAlign="Right" format="C2"
                    [visible]="showOwed()">
            <ng-template #template let-data>
              @if (data.paymentScheduled && data.owedTotal > 0) {
                <span class="money-scheduled"
                      [attr.title]="data.nextChargeDate ? 'Auto-pay scheduled — next charge ' + (data.nextChargeDate | date:'mediumDate') : 'Auto-pay scheduled'">
                  <i class="bi bi-calendar-event scheduled-icon"></i>{{ data.owedTotal | currency }}
                </span>
              } @else {
                <span [style.color]="data.owedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.owedTotal > 0">
                  {{ data.owedTotal | currency }}
                </span>
              }
            </ng-template>
          </e-column>
          <e-column [field]="procFeeField()" [headerText]="procFeeHeader()" width="75" textAlign="Right" format="C2"
                    [visible]="showProcessing()"></e-column>
          <e-column field="feeAdj" headerText="Fee-Adj" width="90" textAlign="Right" format="C2"
                    [visible]="showFeeAdj()">
            <!-- The grid renders header templates as static HTML (Angular events never
                 fire here) and clips with overflow:hidden, so the styled popover is driven
                 imperatively from onDataBound (wireFeeAdjInfo) against a body-mounted
                 position:fixed panel that escapes the clip. Text lives here via data-help. -->
            <ng-template #headerTemplate>
              <span>Fee-Adj<i class="bi bi-info-circle text-info ms-1 fee-adj-info" tabindex="0"
                              data-help="Correction Record, Early Bird Discount, Late Fee or Discount Code amount applied"></i></span>
            </ng-template>
            <ng-template #template let-data>
              <span [class.text-success]="data.feeAdj < 0">{{ data.feeAdj | currency }}</span>
            </ng-template>
          </e-column>
          <e-column field="ccOwedTotal" headerText="CC Owed" width="75" textAlign="Right"
                    [visible]="showCcOwed()">
            <ng-template #template let-data>
              <span [style.color]="data.ccOwedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.ccOwedTotal > 0">
                {{ data.ccOwedTotal | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="ckOwedTotal" headerText="Check Owed" width="75" textAlign="Right"
                    [visible]="showCkOwed()">
            <ng-template #template let-data>
              <span [style.color]="data.ckOwedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.ckOwedTotal > 0">
                {{ data.ckOwedTotal | currency }}
              </span>
            </ng-template>
          </e-column>
        </e-columns>
        <e-aggregates>
          <e-aggregate>
            <e-columns>
              <e-column field="teamName" type="Custom">
                <ng-template #footerTemplate>
                  <strong>{{ teams().length }} {{ teams().length === 1 ? 'team' : 'teams' }}</strong>
                </ng-template>
              </e-column>
              <e-column field="feeTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumFee() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="deposit" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumDeposit() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="balanceDue" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumBalanceDue() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="tenderPaid" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumPaid() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="depositDue" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumDepositDue() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="additionalDue" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumAdditionalDue() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="owedTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [style.color]="sumOwed() > 0 ? 'var(--bs-danger)' : ''">
                    {{ sumOwed() | currency }}
                  </div>
                </ng-template>
              </e-column>
              <e-column [field]="procFeeField()" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumProcessing() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="feeAdj" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [class.text-success]="sumFeeAdj() < 0">{{ sumFeeAdj() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="ccOwedTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [style.color]="sumCcOwed() > 0 ? 'var(--bs-danger)' : 'var(--bs-success)'">
                    {{ sumCcOwed() | currency }}
                  </div>
                </ng-template>
              </e-column>
              <e-column field="ckOwedTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [style.color]="sumCkOwed() > 0 ? 'var(--bs-danger)' : 'var(--bs-success)'">
                    {{ sumCkOwed() | currency }}
                  </div>
                </ng-template>
              </e-column>
            </e-columns>
          </e-aggregate>
        </e-aggregates>
      </ejs-grid>
    `,
    styles: [`
      .aggregate-value {
        font-weight: var(--font-weight-bold);
        text-align: right;
      }

      .team-name-cell {
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }

      .agegroup-cell {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
      }

      /* Waitlist marker — a twin team lives in a "WAITLIST - {agegroup}" mirror age group.
         We show the real age group it's waitlisted under and badge it, rather than the
         mangled prefixed name, so the column reads honestly and fits its width. */
      .wl-badge {
        flex-shrink: 0;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        line-height: 1;
        letter-spacing: 0.02em;
        padding: 2px var(--space-1);
        border-radius: var(--radius-sm);
        color: var(--bs-warning-text-emphasis);
        background: rgba(var(--bs-warning-rgb), 0.15);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.4);
        cursor: default;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* Allow Team-column cells to wrap to two lines when the column is narrow.
         Syncfusion's default cell white-space is nowrap; this override is scoped
         to cells flagged with customAttributes.class = 'team-name-wrap-cell' so
         it doesn't bleed to other columns. ::ng-deep needed because the td is
         rendered by Syncfusion outside this component's encapsulated scope. */
      :host ::ng-deep .e-grid td.team-name-wrap-cell {
        white-space: normal;
        line-height: 1.2;
      }

      .btn-inline-remove {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border: none;
        background: transparent;
        color: var(--brand-text-muted);
        border-radius: var(--radius-sm);
        cursor: pointer;
        font-size: var(--font-size-xs);
        flex-shrink: 0;
        transition: color 0.1s ease, background-color 0.1s ease;

        &:hover { color: var(--bs-danger); background: rgba(var(--bs-danger-rgb), 0.08); }
        &:disabled { opacity: 0.4; cursor: default; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisteredTeamsGridComponent {
    readonly teams = input.required<RegisteredTeamDto[]>();

    // Column visibility flags
    readonly showStructure = input(false); // immutable fee structure (Deposit + Balance Due) — Teams step
    readonly showDeposit = input(false);   // net-of-paid deposit (DepositDue) — Payment step
    readonly showBalance = input(false);   // net-of-paid balance (AdditionalDue) — Payment step
    readonly showOwed = input(false);
    readonly showProcessing = input(false);
    readonly showPaid = input(true);
    readonly showCcOwed = input(true);
    readonly showCkOwed = input(true);
    readonly showLop = input(false);
    readonly showAgeGroup = input(true);
    readonly showRegDate = input(true);
    readonly showTotalFee = input(true);
    // Admin fly-ins force the Fee-Adj column visible even when every row is $0, so an absent
    // column unambiguously means "no adjustment" rather than a missed render. Registration
    // wizard grids leave this false → conditional (shown only when some row is non-zero).
    readonly alwaysShowFeeAdj = input(false);
    readonly procFeeHeader = input('Proc Fee');
    readonly teamColHeader = input('Team');
    // Which field the Proc Fee column/aggregate renders. Teams show 'feeProcessingDue' (proc
    // still owed if CC-billed); the family statement shows 'feeProcessing' (the statement-of-fact
    // proc read off the registration), so Total Fee + Proc reconciles with what was paid.
    readonly procFeeField = input<'feeProcessingDue' | 'feeProcessing'>('feeProcessingDue');
    readonly showRemove = input(false);
    readonly actionInProgress = input(false);
    readonly frozenTeamCol = input(false);
    readonly teamColWidth = input(160);
    readonly pageSize = input(0);
    readonly gridHeight = input<string | number>('auto');

    // Events
    readonly removeTeam = output<RegisteredTeamDto>();

    // Conditional column visibility — surface the unified Fee-Adj column when any team carries a
    // non-zero net adjustment (discount, late fee, or correction — any sign).
    readonly showFeeAdj = computed(() => this.alwaysShowFeeAdj() || this.teams().some(t => (t.feeAdj ?? 0) !== 0));

    // Last column-visibility state we rebuilt the header for. Seeded to match the
    // initial all-hidden render so the first dataBound is a no-op.
    private lastColVis = { feeAdj: false };

    // Body-mounted styled popover for the Fee-Adj header "i". See wireFeeAdjInfo.
    private feeAdjPopover: HTMLElement | null = null;
    private feeAdjReposition: (() => void) | null = null;

    constructor() {
        inject(DestroyRef).onDestroy(() => this.teardownFeeAdjInfo());
    }

    /**
     * Imperatively wire the Fee-Adj header "i" to a styled hover popover.
     *
     * Why not a template-driven tooltip: Syncfusion renders header templates as
     * static HTML, so Angular (mouseenter)/(click) bindings never fire there, and
     * the header clips with overflow:hidden while scrolling horizontally — so an
     * in-header position:absolute panel is invisible. We instead attach native
     * listeners (which DO fire from component code) to the rendered icon and show a
     * position:fixed panel mounted on <body> that escapes the clip. The panel reuses
     * the global .hover-popover-panel styles so it matches the ledger card exactly.
     *
     * Idempotent: the icon element is recreated whenever the header rebuilds
     * (refreshColumns), so we re-bind each dataBound and guard with a dataset flag.
     */
    private wireFeeAdjInfo(grid: GridComponent): void {
        const icon = grid.element?.querySelector<HTMLElement>('.fee-adj-info');
        if (!icon || icon.dataset['popoverWired'] === '1') return;
        icon.dataset['popoverWired'] = '1';

        const place = () => {
            const panel = this.feeAdjPopover;
            if (!panel) return;
            const r = icon.getBoundingClientRect();
            panel.style.top = `${r.bottom + 6}px`;
            const left = Math.min(r.left, window.innerWidth - panel.offsetWidth - 8);
            panel.style.left = `${Math.max(8, left)}px`;
        };

        const show = () => {
            const panel = this.ensureFeeAdjPopover();
            const text = panel.querySelector('.hover-popover-text');
            if (text) text.textContent = icon.dataset['help'] ?? '';
            panel.style.display = 'block';
            place();
            this.feeAdjReposition = place;
            window.addEventListener('scroll', place, true);
            window.addEventListener('resize', place);
        };

        const hide = () => {
            if (this.feeAdjPopover) this.feeAdjPopover.style.display = 'none';
            if (this.feeAdjReposition) {
                window.removeEventListener('scroll', this.feeAdjReposition, true);
                window.removeEventListener('resize', this.feeAdjReposition);
                this.feeAdjReposition = null;
            }
        };

        icon.addEventListener('mouseenter', show);
        icon.addEventListener('mouseleave', hide);
        icon.addEventListener('focus', show);
        icon.addEventListener('blur', hide);
    }

    private ensureFeeAdjPopover(): HTMLElement {
        if (this.feeAdjPopover) return this.feeAdjPopover;
        const el = document.createElement('div');
        el.className = 'hover-popover-panel';
        el.style.position = 'fixed';
        el.style.right = 'auto';
        el.style.zIndex = '2000';
        el.style.display = 'none';
        el.innerHTML =
            '<div class="hover-popover-header"><span class="hover-popover-title">Fee-Adj</span></div>' +
            '<div class="hover-popover-body"><span class="hover-popover-text"></span></div>';
        document.body.appendChild(el);
        this.feeAdjPopover = el;
        return el;
    }

    private teardownFeeAdjInfo(): void {
        if (this.feeAdjReposition) {
            window.removeEventListener('scroll', this.feeAdjReposition, true);
            window.removeEventListener('resize', this.feeAdjReposition);
            this.feeAdjReposition = null;
        }
        this.feeAdjPopover?.remove();
        this.feeAdjPopover = null;
    }

    /**
     * Runs after every dataBound — i.e. after the grid has finished rendering the
     * current data. Stamps row numbers, then resyncs the header if a conditional
     * column just toggled.
     *
     * Why here and not on a reactive binding: frozen-column grids
     * (frozenTeamCol=true) split header and content into separate frozen/movable
     * tables. When a column's `visible` flips at runtime — e.g. Discount turns on
     * after a code is applied — Syncfusion rebuilds the CONTENT but can leave the
     * movable HEADER stale, so header labels drift one column out of step with the
     * cells. refreshColumns() rebuilds the header, but it MUST run after the data
     * render settles; firing it mid-change-detection raced the async data refresh,
     * which then re-clobbered the header. dataBound is that settled point.
     */
    onDataBound(grid: GridComponent): void {
        this.refreshRowNumbers(grid);
        this.wireFeeAdjInfo(grid);

        const feeAdj = this.showFeeAdj();
        if (feeAdj === this.lastColVis.feeAdj) return;
        // Record BEFORE refreshColumns so the dataBound it triggers sees no change
        // and returns early — that's the loop guard.
        this.lastColVis = { feeAdj };
        grid.refreshColumns();
    }

    /** Stamp 1-based row numbers in the unbound `#` column. Re-runs on dataBound + sort/page actions. */
    refreshRowNumbers(grid: GridComponent): void {
        grid.getRows().forEach((row, i) => {
            const cell = row.querySelector('td.row-number-cell');
            if (cell) cell.textContent = String(i + 1);
        });
    }

    onActionComplete(args: { requestType?: string }, grid: GridComponent): void {
        if (args.requestType === 'sorting' || args.requestType === 'paging' || args.requestType === 'refresh') {
            this.refreshRowNumbers(grid);
        }
    }

    // Waitlist twins have no flag on the DTO — their status lives only in the name,
    // minted "WAITLIST - {name}" by the backend (TeamPlacementService). This is the
    // one place the frontend knows that convention; keep it here, not inline in the
    // template. If reused elsewhere, promote to a backend `isWaitlist` flag instead.
    private readonly WAITLIST_PREFIX = 'WAITLIST - ';

    isWaitlisted(name: string | null | undefined): boolean {
        return !!name && name.startsWith(this.WAITLIST_PREFIX);
    }

    /** Drop the "WAITLIST - " prefix for display — the WL badge carries the status.
     *  Applies to both the team name and the (identically-prefixed) age-group name. */
    stripWaitlist(name: string | null | undefined): string {
        return this.isWaitlisted(name) ? name!.slice(this.WAITLIST_PREFIX.length) : (name ?? '');
    }

    /**
     * LOP display: strip parenthetical/textual modifier from a numbered value.
     * "5 (strongest)" → "5"; "Recreational" → "Recreational" (passthrough).
     * Full string is preserved on the cell as a tooltip via [attr.title].
     */
    formatLop(lop: string | null | undefined): string {
        if (!lop) return '';
        const match = lop.match(/^\s*(\d+)/);
        return match ? match[1] : lop;
    }

    // Aggregates
    readonly sumFee = computed(() => this.teams().reduce((s, t) => s + t.deposit + t.balanceDue, 0));
    readonly sumPaid = computed(() => this.teams().reduce((s, t) => s + t.tenderPaid, 0));
    readonly sumDeposit = computed(() => this.teams().reduce((s, t) => s + t.deposit, 0));
    readonly sumBalanceDue = computed(() => this.teams().reduce((s, t) => s + t.balanceDue, 0));
    readonly sumDepositDue = computed(() => this.teams().reduce((s, t) => s + t.depositDue, 0));
    readonly sumAdditionalDue = computed(() => this.teams().reduce((s, t) => s + t.additionalDue, 0));
    readonly sumOwed = computed(() => this.teams().reduce((s, t) => s + t.owedTotal, 0));
    readonly sumProcessing = computed(() => this.procFeeField() === 'feeProcessing'
        ? this.teams().reduce((s, t) => s + (t.feeProcessing ?? 0), 0)
        : this.teams().reduce((s, t) => s + (t.feeProcessingDue ?? 0), 0));
    readonly sumFeeAdj = computed(() => this.teams().reduce((s, t) => s + (t.feeAdj ?? 0), 0));
    readonly sumCcOwed = computed(() => this.teams().reduce((s, t) => s + t.ccOwedTotal, 0));
    readonly sumCkOwed = computed(() => this.teams().reduce((s, t) => s + t.ckOwedTotal, 0));
}
