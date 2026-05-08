import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { RegisteredTeamDto } from '@core/api';
import { InfoTooltipComponent } from '../../../../shared-ui/components/info-tooltip.component';

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
    imports: [CurrencyPipe, GridAllModule, InfoTooltipComponent],
    template: `
      <ejs-grid #grid [dataSource]="teams()" [allowSorting]="true"
                [allowTextWrap]="true"
                [textWrapSettings]="{ wrapMode: 'Header' }"
                [rowHeight]="30"
                [height]="gridHeight()"
                [allowPaging]="pageSize() > 0"
                [pageSettings]="{ pageSize: pageSize() || 50 }"
                (dataBound)="refreshRowNumbers(grid)"
                (actionComplete)="onActionComplete($event, grid)"
                cssClass="tsic-grid-compact">
        <e-columns>
          <!-- Row number (unbound — stamped via refreshRowNumbers, survives sort) -->
          <e-column headerText="" width="40" textAlign="Center" [allowSorting]="false"
                    [isFrozen]="frozenTeamCol()"
                    [customAttributes]="{ class: 'row-number-cell' }"></e-column>
          <e-column field="teamName" headerText="Team" [width]="teamColWidth()"
                    [isFrozen]="frozenTeamCol()">
            <ng-template #template let-data>
              <span class="team-name-cell">
                @if (showRemove() && data.paidTotal === 0) {
                  <button type="button" class="btn-inline-remove"
                          [disabled]="actionInProgress()"
                          (click)="removeTeam.emit(data)"
                          title="Remove {{ data.teamName }} from event">
                    <i class="bi bi-trash3"></i>
                  </button>
                }
                <span class="fw-semibold">{{ data.teamName }}</span>
              </span>
            </ng-template>
          </e-column>
          <e-column field="ageGroupName" headerText="Age Group" width="75"></e-column>
          <e-column field="levelOfPlay" headerText="LOP" width="55" textAlign="Center" [visible]="showLop()">
            <ng-template #template let-data>
              <span [attr.title]="data.levelOfPlay">{{ formatLop(data.levelOfPlay) }}</span>
            </ng-template>
          </e-column>
          <e-column field="registrationTs" headerText="Reg Date" width="100" type="date" format="yMd"
                    [visible]="showRegDate()"></e-column>
          <e-column field="deposit" headerText="Deposit" width="85" textAlign="Right" format="C2"
                    [visible]="showStructure()"></e-column>
          <e-column field="balanceDue" headerText="Balance Due" width="100" textAlign="Right" format="C2"
                    [visible]="showStructure()"></e-column>
          <e-column field="depositDue" headerText="Deposit Due" width="90" textAlign="Right" format="C2"
                    [visible]="showDeposit()"></e-column>
          <e-column field="additionalDue" headerText="Bal Due" width="80" textAlign="Right" format="C2"
                    [visible]="showBalance()"></e-column>
          <!-- Total Fee = structural sum (Deposit + BalanceDue), not feeTotal which is
               phase-aware (deposit-phase total = deposit + processing). The field stays
               as 'feeTotal' so the aggregate footer aligns under this column; the cell
               template renders the structural sum instead of the bound value. -->
          <e-column field="feeTotal" headerText="Total Fee" width="80" textAlign="Right" [allowSorting]="false">
            <ng-template #template let-data>
              {{ (data.deposit + data.balanceDue) | currency }}
            </ng-template>
          </e-column>
          <e-column field="paidTotal" headerText="Paid" width="90" textAlign="Right" format="C2"
                    [visible]="showPaid()">
            <ng-template #template let-data>
              <span [class.text-success]="data.paidTotal > 0" [class.text-muted]="data.paidTotal === 0">
                {{ data.paidTotal | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="owedTotal" headerText="Owed" width="80" textAlign="Right" format="C2"
                    [visible]="showOwed()">
            <ng-template #template let-data>
              <span [style.color]="data.owedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.owedTotal > 0">
                {{ data.owedTotal | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="feeProcessing" headerText="Proc Fee" width="85" textAlign="Right" format="C2"
                    [visible]="showProcessing()"></e-column>
          <e-column field="feeDiscount" headerText="Discount" width="90" textAlign="Right" format="C2"
                    [visible]="showDiscount()">
            <ng-template #template let-data>
              <span [class.text-success]="data.feeDiscount > 0">
                {{ data.feeDiscount > 0 ? '-' : '' }}{{ data.feeDiscount | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="feeLatefee" headerText="Fee-Adj" width="90" textAlign="Right" format="C2"
                    [visible]="showFeeAdj()">
            <ng-template #headerTemplate>
              <span>Fee-Adj<app-info-tooltip message="Depending on when you registered, this may show an early-bird discount (negative value) or a late fee (positive value). Only one applies."></app-info-tooltip></span>
            </ng-template>
          </e-column>
          <e-column field="ccOwedTotal" headerText="CC Owed" width="90" textAlign="Right"
                    [visible]="showCcOwed()">
            <ng-template #template let-data>
              <span [style.color]="data.ccOwedTotal > 0 ? 'var(--bs-danger)' : ''" [class.fw-semibold]="data.ccOwedTotal > 0">
                {{ data.ccOwedTotal | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="ckOwedTotal" headerText="Check Owed" width="110" textAlign="Right"
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
              <e-column field="paidTotal" type="Sum" format="C2">
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
              <e-column field="feeProcessing" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumProcessing() | currency }}</div>
                </ng-template>
              </e-column>
              <e-column field="feeDiscount" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value" [class.text-success]="sumDiscount() > 0">
                    {{ sumDiscount() > 0 ? '-' : '' }}{{ sumDiscount() | currency }}
                  </div>
                </ng-template>
              </e-column>
              <e-column field="feeLatefee" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumFeeAdj() | currency }}</div>
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
    readonly showRegDate = input(true);
    readonly showRemove = input(false);
    readonly actionInProgress = input(false);
    readonly frozenTeamCol = input(false);
    readonly teamColWidth = input(160);
    readonly pageSize = input(0);
    readonly gridHeight = input<string | number>('auto');

    // Events
    readonly removeTeam = output<RegisteredTeamDto>();

    // Conditional column visibility — only surface Discount / Fee-Adj when any team has non-zero value
    readonly showDiscount = computed(() => this.teams().some(t => (t.feeDiscount ?? 0) > 0));
    readonly showFeeAdj = computed(() => this.teams().some(t => (t.feeLatefee ?? 0) > 0));

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
    readonly sumPaid = computed(() => this.teams().reduce((s, t) => s + t.paidTotal, 0));
    readonly sumDeposit = computed(() => this.teams().reduce((s, t) => s + t.deposit, 0));
    readonly sumBalanceDue = computed(() => this.teams().reduce((s, t) => s + t.balanceDue, 0));
    readonly sumDepositDue = computed(() => this.teams().reduce((s, t) => s + t.depositDue, 0));
    readonly sumAdditionalDue = computed(() => this.teams().reduce((s, t) => s + t.additionalDue, 0));
    readonly sumOwed = computed(() => this.teams().reduce((s, t) => s + t.owedTotal, 0));
    readonly sumProcessing = computed(() => this.teams().reduce((s, t) => s + (t.feeProcessing ?? 0), 0));
    readonly sumDiscount = computed(() => this.teams().reduce((s, t) => s + (t.feeDiscount ?? 0), 0));
    readonly sumFeeAdj = computed(() => this.teams().reduce((s, t) => s + (t.feeLatefee ?? 0), 0));
    readonly sumCcOwed = computed(() => this.teams().reduce((s, t) => s + t.ccOwedTotal, 0));
    readonly sumCkOwed = computed(() => this.teams().reduce((s, t) => s + t.ckOwedTotal, 0));
}
