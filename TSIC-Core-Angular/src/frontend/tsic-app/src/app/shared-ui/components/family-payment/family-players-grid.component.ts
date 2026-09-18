import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import { GridRowNumbersDirective } from '@shared-ui/directives/grid-row-numbers.directive';
import { GridHeaderInfoPopover } from '@shared-ui/grid/grid-header-info-popover';
import type { RegisteredPlayerLineDto } from '@core/api';

/**
 * Family accounting breakdown grid — one row per CHILD REGISTRATION.
 *
 * The player-shaped peer of `app-registered-teams-grid`, deliberately a separate component.
 * The two grids show the same MONEY columns (and the backend computes those through the same
 * canonical helpers, so they can never disagree about a dollar), but they identify their rows
 * completely differently: a team has a name, an age group and a level of play; a player has a
 * name, the team they were placed on, and a waitlist status.
 *
 * Previously this screen borrowed the teams grid, which meant the backend had to dress every
 * player as a team. That cost two real defects: `AgeGroupName` was blanked (because this screen
 * hid that column) which silently killed the waitlist badge, and the event label had to be
 * scraped back out of accounting records — failing for a registration with no records yet.
 * Both are gone here: the row carries its own player name, team, and age group.
 */
@Component({
    selector: 'app-family-players-grid',
    standalone: true,
    imports: [CurrencyPipe, GridAllModule, GridRowNumbersDirective],
    template: `
      <ejs-grid #grid tsicRowNumbers [tsicRowNumbersOffset]="0"
                [dataSource]="gridRows()" [allowSorting]="true"
                [allowTextWrap]="true"
                [textWrapSettings]="{ wrapMode: 'Header' }"
                height="auto"
                (dataBound)="onDataBound(grid)"
                cssClass="tsic-grid-compact">
        <e-columns>
          <!-- Row number (unbound — stamped by tsicRowNumbers) -->
          <e-column headerText="" width="30" textAlign="Center" [allowSorting]="false"
                    [isFrozen]="true"
                    [customAttributes]="{ class: 'row-number-cell' }"></e-column>

          <e-column field="playerName" headerText="Player" width="160"
                    [isFrozen]="true"
                    [customAttributes]="{ class: 'player-name-wrap-cell' }">
            <ng-template #template let-data>
              <span class="player-name-cell">
                <span class="player-name-text">{{ data.playerName }}</span>
                <!-- Waitlist marker rides the frozen name cell so it is visible no matter how
                     far the money columns scroll (PL-037). Driven by the backend flag, never a
                     name parse. -->
                @if (data.isWaitlisted) {
                  <span class="wl-badge" tabindex="0"
                        [attr.title]="'Waitlisted under ' + data.ageGroupDisplayName + ' — placed when a roster spot opens'">WL</span>
                }
              </span>
            </ng-template>
          </e-column>

          <!-- What the registration is FOR. Built from the row's own fields, so a registration
               with no accounting records yet still labels correctly. -->
          <e-column field="teamName" headerText="Event" width="170" [allowSorting]="false">
            <ng-template #template let-data>
              <span class="event-cell">{{ eventLabel(data) }}</span>
            </ng-template>
          </e-column>

          <e-column field="tenderPaid" headerText="Paid" width="75" textAlign="Right" format="C2">
            <ng-template #template let-data>
              <span [class.text-success]="data.tenderPaid > 0" [class.text-muted]="data.tenderPaid === 0">
                {{ data.tenderPaid | currency }}
              </span>
            </ng-template>
          </e-column>

          <e-column field="depositDue" headerText="Deposit Due" width="75" textAlign="Right" format="C2"
                    [visible]="showDepositColumns()"></e-column>
          <e-column field="additionalDue" headerText="Balance Due" width="75" textAlign="Right" format="C2"
                    [visible]="showDepositColumns()"></e-column>

          <!-- Total Fee = structural sum (Deposit + BalanceDue), NOT feeTotal, which is
               phase-aware. The field stays 'feeTotal' so the aggregate footer aligns under
               this column; the cell renders the structural sum. -->
          <e-column field="feeTotal" headerText="Total Fee" width="80" textAlign="Right" [allowSorting]="false">
            <ng-template #template let-data>
              {{ (data.deposit + data.balanceDue) | currency }}
            </ng-template>
          </e-column>

          <e-column field="owedTotal" headerText="Owed" width="80" textAlign="Right" format="C2">
            <ng-template #template let-data>
              <span [style.color]="data.owedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.owedTotal > 0">
                {{ data.owedTotal | currency }}
              </span>
            </ng-template>
          </e-column>

          <e-column field="feeProcessing" headerText="Proc Fee" width="75" textAlign="Right" format="C2"></e-column>

          <e-column field="feeAdj" headerText="Fee-Adj" width="90" textAlign="Right" format="C2">
            <!-- Header templates render as static HTML (Angular events never fire here) and the
                 header clips, so the popover is wired imperatively from onDataBound against a
                 body-mounted panel. Text lives here via data-help. -->
            <ng-template #headerTemplate>
              <span>Fee-Adj<i class="bi bi-info-circle text-info ms-1 fee-adj-info" tabindex="0"
                              data-help="Correction Record, Early Bird Discount, Late Fee or Discount Code amount applied"></i></span>
            </ng-template>
            <ng-template #template let-data>
              <span [style.color]="data.feeAdj < 0 ? 'var(--brand-success)' : data.feeAdj > 0 ? 'var(--brand-danger)' : ''">{{ data.feeAdj | currency }}</span>
            </ng-template>
          </e-column>

          <e-column field="ccOwedTotal" headerText="CC Owed" width="75" textAlign="Right" format="C2">
            <ng-template #template let-data>
              <span [style.color]="data.ccOwedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.ccOwedTotal > 0">
                {{ data.ccOwedTotal | currency }}
              </span>
            </ng-template>
          </e-column>

          <e-column field="ckOwedTotal" headerText="Check Owed" width="75" textAlign="Right" format="C2"
                    [visible]="showCheckOwed()">
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
              <e-column field="playerName" type="Custom">
                <ng-template #footerTemplate>
                  <strong>{{ rows().length }} {{ rows().length === 1 ? 'registration' : 'registrations' }}</strong>
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
              <e-column field="feeTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumFee() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="owedTotal" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [style.color]="sumOwed() > 0 ? 'var(--bs-danger)' : ''">
                    {{ sumOwed() | currency }}
                  </div>
                </ng-template>
              </e-column>
              <e-column field="feeProcessing" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumProcessing() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="feeAdj" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [style.color]="sumFeeAdj() < 0 ? 'var(--brand-success)' : sumFeeAdj() > 0 ? 'var(--brand-danger)' : ''">{{ sumFeeAdj() | currency }}</div>
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

      .player-name-cell {
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }

      .player-name-text { min-width: 0; }

      .event-cell {
        color: var(--brand-text-muted);
      }

      /* Waitlist marker — frozen name cell, always visible however far the money columns
         scroll. Driven by the backend isWaitlisted flag. */
      .wl-badge {
        flex-shrink: 0;
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        line-height: 1;
        padding: 1px var(--space-1);
        border-radius: var(--radius-sm);
        color: var(--bs-warning-text-emphasis);
        background: rgba(var(--bs-warning-rgb), 0.15);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.4);
        cursor: default;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* Header wrap ('Header' wrapMode) breaks words mid-word on narrow columns
         ("Deposi / t Due") because Syncfusion's e-wrap sets word-wrap: break-word.
         Restrict header wrapping to word boundaries. */
      :host ::ng-deep .e-grid .e-columnheader .e-headercelldiv {
        word-wrap: normal;
        overflow-wrap: normal;
        word-break: keep-all;
      }

      /* Let the Player column wrap to two lines. Syncfusion's default cell white-space is
         nowrap; scoped to cells flagged via customAttributes so it can't bleed. ::ng-deep
         is required — the td is rendered by Syncfusion outside this component's scope. */
      :host ::ng-deep .e-grid td.player-name-wrap-cell {
        white-space: normal;
        line-height: 1.2;
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyPlayersGridComponent {
    readonly rows = input.required<RegisteredPlayerLineDto[]>();

    /** Deposit Due / Balance Due only make sense when the fees actually carry a deposit. */
    readonly showDepositColumns = input(false);

    /** CC-only jobs can never take a check, so the Check Owed column is pure noise there. */
    readonly showCheckOwed = input(true);

    private readonly feeAdjInfo = new GridHeaderInfoPopover('.fee-adj-info', 'Fee-Adj');

    constructor() {
        inject(DestroyRef).onDestroy(() => this.feeAdjInfo.destroy());
    }

    onDataBound(grid: GridComponent): void {
        this.feeAdjInfo.wire(grid.element);
    }

    /**
     * What the registration is for: "AgeGroup · Team", falling back to whichever half exists.
     * Uses the prefix-stripped age group — a waitlisted child reads "U12 · Thunder" with the
     * WL badge carrying the status, rather than "WAITLIST - U12" shouting it twice.
     */
    eventLabel(row: RegisteredPlayerLineDto): string {
        const team = row.teamName?.trim();
        const ageGroup = row.ageGroupDisplayName?.trim();
        if (ageGroup && team) return `${ageGroup} · ${team}`;
        return team || ageGroup || '—';
    }

    readonly gridRows = computed(() => [...this.rows()]);

    // Aggregates. sumFee follows the Total Fee cell (structural), not feeTotal.
    readonly sumFee = computed(() => this.rows().reduce((s, r) => s + r.deposit + r.balanceDue, 0));
    readonly sumPaid = computed(() => this.rows().reduce((s, r) => s + r.tenderPaid, 0));
    readonly sumDepositDue = computed(() => this.rows().reduce((s, r) => s + r.depositDue, 0));
    readonly sumAdditionalDue = computed(() => this.rows().reduce((s, r) => s + r.additionalDue, 0));
    readonly sumOwed = computed(() => this.rows().reduce((s, r) => s + r.owedTotal, 0));
    readonly sumProcessing = computed(() => this.rows().reduce((s, r) => s + r.feeProcessing, 0));
    readonly sumFeeAdj = computed(() => this.rows().reduce((s, r) => s + (r.feeAdj ?? 0), 0));
    readonly sumCcOwed = computed(() => this.rows().reduce((s, r) => s + r.ccOwedTotal, 0));
    readonly sumCkOwed = computed(() => this.rows().reduce((s, r) => s + r.ckOwedTotal, 0));
}
