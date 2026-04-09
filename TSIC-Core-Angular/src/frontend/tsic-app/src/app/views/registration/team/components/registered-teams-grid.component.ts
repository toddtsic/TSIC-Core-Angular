import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
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
    imports: [CurrencyPipe, GridAllModule],
    template: `
      <ejs-grid [dataSource]="teams()" [allowSorting]="true"
                [allowTextWrap]="true"
                [textWrapSettings]="{ wrapMode: 'Header' }"
                [rowHeight]="30"
                [allowPaging]="pageSize() > 0"
                [pageSettings]="{ pageSize: pageSize() || 50 }"
                cssClass="tsic-grid-compact">
        <e-columns>
          <e-column field="teamName" headerText="Team" [width]="teamColWidth()"
                    [isFrozen]="frozenTeamCol()">
            <ng-template #template let-data>
              <span class="team-name-cell">
                @if (showRemove() && data.paidTotal === 0) {
                  <button type="button" class="btn-inline-remove"
                          [disabled]="actionInProgress()"
                          (click)="removeTeam.emit(data)"
                          title="Remove {{ data.teamName }}">
                    <i class="bi bi-trash3"></i>
                  </button>
                }
                <span class="fw-semibold">{{ data.teamName }}</span>
                @if (data.levelOfPlay) {
                  <span class="select-lop ms-1">LOP {{ data.levelOfPlay }}</span>
                }
              </span>
            </ng-template>
          </e-column>
          <e-column field="ageGroupName" headerText="Age Group" width="95"></e-column>
          <e-column field="feeBase" headerText="Fee" width="90" textAlign="Right" format="C2"></e-column>
          <e-column field="paidTotal" headerText="Paid" width="90" textAlign="Right" format="C2"
                    [visible]="showPaid()">
            <ng-template #template let-data>
              <span [class.text-success]="data.paidTotal > 0" [class.text-muted]="data.paidTotal === 0">
                {{ data.paidTotal | currency }}
              </span>
            </ng-template>
          </e-column>
          <e-column field="depositDue" headerText="Deposit Due" width="110" textAlign="Right" format="C2"
                    [visible]="showDeposit()"></e-column>
          <e-column field="additionalDue" headerText="Additional Due" width="120" textAlign="Right" format="C2"
                    [visible]="showDeposit()"></e-column>
          <e-column field="feeProcessing" headerText="Proc Fee" width="85" textAlign="Right" format="C2"
                    [visible]="showProcessing()"></e-column>
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
              <e-column field="feeBase" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumFee() | currency }}</div>
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
              <e-column field="feeProcessing" type="Sum" format="C2">
                <ng-template #footerTemplate let-data>
                  <div class="aggregate-value">{{ sumProcessing() | currency }}</div>
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
    readonly showDeposit = input(false);
    readonly showProcessing = input(false);
    readonly showPaid = input(true);
    readonly showCcOwed = input(true);
    readonly showCkOwed = input(true);
    readonly showRemove = input(false);
    readonly actionInProgress = input(false);
    readonly frozenTeamCol = input(false);
    readonly teamColWidth = input(160);
    readonly pageSize = input(0);

    // Events
    readonly removeTeam = output<RegisteredTeamDto>();

    // Aggregates
    readonly sumFee = computed(() => this.teams().reduce((s, t) => s + t.feeBase, 0));
    readonly sumPaid = computed(() => this.teams().reduce((s, t) => s + t.paidTotal, 0));
    readonly sumDepositDue = computed(() => this.teams().reduce((s, t) => s + t.depositDue, 0));
    readonly sumAdditionalDue = computed(() => this.teams().reduce((s, t) => s + t.additionalDue, 0));
    readonly sumProcessing = computed(() => this.teams().reduce((s, t) => s + (t.feeProcessing ?? 0), 0));
    readonly sumCcOwed = computed(() => this.teams().reduce((s, t) => s + t.ccOwedTotal, 0));
    readonly sumCkOwed = computed(() => this.teams().reduce((s, t) => s + t.ckOwedTotal, 0));
}
