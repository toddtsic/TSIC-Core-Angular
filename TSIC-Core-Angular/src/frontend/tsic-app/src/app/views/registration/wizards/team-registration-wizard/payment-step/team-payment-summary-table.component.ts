import { ChangeDetectionStrategy, Component, inject, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TeamPaymentService } from '../services/team-payment.service';
import {
  GridModule,
  GridComponent,
  QueryCellInfoEventArgs,
  SortService,
  ExcelExportService,
} from '@syncfusion/ej2-angular-grids';

/**
 * Team payment summary grid - displays registered teams with fees and balances using Syncfusion Grid.
 * Matches production jqGrid layout with row numbers and aggregate footer.
 */
@Component({
  selector: 'app-team-payment-summary-table',
  standalone: true,
  imports: [CommonModule, GridModule],
  providers: [SortService, ExcelExportService],
  template: `
    <section
      class="p-3 p-sm-4 mb-3 rounded-3"
      aria-labelledby="team-pay-summary-title"
      style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)"
    >
      <h6 id="team-pay-summary-title" class="fw-semibold mb-2">
        Team Payment Summary
      </h6>

      <!-- Payment Method Selector (show when both CC and Check are allowed) -->
      @if (svc.showPaymentMethodSelector()) {
        <div class="mb-3">
          <label for="paymentMethod" class="form-label fw-semibold"
            >How will you be paying?</label
          >
          <select
            id="paymentMethod"
            class="form-select"
            [value]="svc.selectedPaymentMethod()"
            (change)="svc.selectedPaymentMethod.set($any($event.target).value)"
          >
            <option value="CC">Credit Card</option>
            <option value="Check">Check</option>
          </select>
          @if (svc.selectedPaymentMethod() === 'Check') {
            <div class="form-text">
              <i class="bi bi-info-circle me-1"></i>
              Processing fees are waived for check payments. You save
              {{ svc.processingFeeSavings() | currency }}!
            </div>
          }
        </div>
      }

      <div
        class="grid-wrapper"
        style="width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch;"
      >
        <ejs-grid
          #grid
          id="paymentGrid"
          [dataSource]="svc.lineItems()"
          [allowSorting]="true"
          [sortSettings]="sortOptions"
          [allowExcelExport]="true"
          height="auto"
          [enableHover]="true"
          [enableAltRow]="true"
          [rowHeight]="30"
          gridLines="Both"
          [autoFit]="true"
          (queryCellInfo)="onQueryCellInfo($event)"
          (dataBound)="onDataBound()"
          class="tight-table"
        >
          <e-columns>
            <!-- Row Number Column -->
            <e-column
              field="rowNum"
              headerText="#"
              width="auto"
              textAlign="Center"
              [allowSorting]="false"
            ></e-column>

            <!-- Age Group Column -->
            <e-column
              field="ageGroup"
              headerText="Age Group"
              width="auto"
            ></e-column>

            <!-- Team Name Column -->
            <e-column
              field="teamName"
              headerText="Team"
              width="auto"
            ></e-column>

            <!-- Level of Play Column -->
            <e-column
              field="levelOfPlay"
              headerText="LOP"
              width="auto"
              textAlign="Center"
            >
              <ng-template #template let-data>
                {{ data.levelOfPlay || '' }}
              </ng-template>
            </e-column>

            <!-- Registration Date Column -->
            <e-column
              field="registrationTs"
              headerText="Reg-Date"
              width="auto"
              textAlign="Center"
              type="date"
              format="MM/dd/yyyy"
            ></e-column>

            <!-- Paid Total Column -->
            <e-column
              field="paidTotal"
              headerText="Paid Total"
              width="auto"
              textAlign="Right"
              format="C2"
            ></e-column>

            <!-- Deposit Due Column -->
            <e-column
              field="depositDue"
              headerText="Deposit Due"
              width="auto"
              textAlign="Right"
              format="C2"
            ></e-column>

            <!-- Additional Due Column -->
            <e-column
              field="additionalDue"
              headerText="Additional Due"
              width="auto"
              textAlign="Right"
              format="C2"
            ></e-column>

            <!-- Fee Processing Column (conditional - hidden when !bAddProcessingFees) -->
            @if (svc.showFeeProcessingColumn()) {
              <e-column
                field="feeProcessing"
                headerText="Fee-Processing"
                width="auto"
                textAlign="Right"
                format="C2"
              ></e-column>
            }

            <!-- CC Owed Total Column (always visible) -->
            <e-column
              field="ccOwedTotal"
              headerText="CC Owed Total"
              width="auto"
              textAlign="Right"
              format="C2"
            ></e-column>

            <!-- Ck Owed Total Column (conditional - hidden when !bAddProcessingFees) -->
            @if (svc.showFeeProcessingColumn()) {
              <e-column
                field="ckOwedTotal"
                headerText="Ck Owed Total"
                width="auto"
                textAlign="Right"
                format="C2"
              ></e-column>
            }
          </e-columns>

          <!-- Aggregates for Footer Totals -->
          <e-aggregates>
            <e-aggregate>
              <e-columns>
                <e-column
                  field="paidTotal"
                  type="Sum"
                  [footerTemplate]="paidTotalTemplate"
                ></e-column>
                <e-column
                  field="depositDue"
                  type="Sum"
                  [footerTemplate]="depositDueTemplate"
                ></e-column>
                <e-column
                  field="additionalDue"
                  type="Sum"
                  [footerTemplate]="additionalDueTemplate"
                ></e-column>
                @if (svc.showFeeProcessingColumn()) {
                  <e-column
                    field="feeProcessing"
                    type="Sum"
                    [footerTemplate]="feeProcessingTemplate"
                  ></e-column>
                }
                <e-column
                  field="ccOwedTotal"
                  type="Sum"
                  [footerTemplate]="ccOwedTemplate"
                ></e-column>
                @if (svc.showFeeProcessingColumn()) {
                  <e-column
                    field="ckOwedTotal"
                    type="Sum"
                    [footerTemplate]="ckOwedTemplate"
                  ></e-column>
                }
              </e-columns>
            </e-aggregate>
          </e-aggregates>
        </ejs-grid>
      </div>

      <!-- Footer Templates -->
      <ng-template #paidTotalTemplate let-data>
        <span class="fw-bold text-success">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #depositDueTemplate let-data>
        <span
          class="fw-bold"
          [class.text-warning]="data.Sum > 0"
          [class.text-muted]="data.Sum === 0"
          >{{ data.Sum | currency }}</span
        >
      </ng-template>
      <ng-template #additionalDueTemplate let-data>
        <span
          class="fw-bold"
          [class.text-warning]="data.Sum > 0"
          [class.text-muted]="data.Sum === 0"
          >{{ data.Sum | currency }}</span
        >
      </ng-template>
      <ng-template #feeProcessingTemplate let-data>
        <span class="fw-bold text-muted">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #ccOwedTemplate let-data>
        <span
          class="fw-bold"
          [class.text-warning]="data.Sum > 0"
          [class.text-muted]="data.Sum === 0"
          >{{ data.Sum | currency }}</span
        >
      </ng-template>
      <ng-template #ckOwedTemplate let-data>
        <span
          class="fw-bold"
          [class.text-warning]="data.Sum > 0"
          [class.text-muted]="data.Sum === 0"
          >{{ data.Sum | currency }}</span
        >
      </ng-template>
    </section>
  `,
  styles: [
    `
      /* Syncfusion table styling for light and dark modes */
      ::ng-deep #paymentGrid .e-headercell {
        background-color: var(--bs-tertiary-bg) !important;
        color: var(--bs-body-color) !important;
        border-color: var(--bs-border-color) !important;
      }

      ::ng-deep #paymentGrid .e-rowcell {
        color: var(--bs-body-color) !important;
        border-color: var(--bs-border-color) !important;
      }

      ::ng-deep #paymentGrid .e-row:not(.e-altrow) .e-rowcell {
        background-color: var(--bs-body-bg) !important;
      }

      ::ng-deep #paymentGrid .e-altrow .e-rowcell {
        background-color: var(--bs-tertiary-bg) !important;
      }

      ::ng-deep #paymentGrid .e-row:hover .e-rowcell {
        background-color: var(--bs-secondary-bg) !important;
      }

      /* Hide Syncfusion aggregate/summary row on mobile only */
      @media (max-width: 767.98px) {
        ::ng-deep #paymentGrid .e-summaryrow,
        ::ng-deep #paymentGrid .e-summarycell {
          display: none !important;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamPaymentSummaryTableComponent {
  readonly svc = inject(TeamPaymentService);

  @ViewChild('grid') public grid!: GridComponent;
  // Sort settings for 2-state sorting (no unsorted state)
  public sortOptions = { allowUnsort: false };
  onQueryCellInfo(args: QueryCellInfoEventArgs): void {
    if (args.column?.field === 'rowNum' && args.data) {
      const index = (this.grid.currentViewData as any[]).findIndex(
        (item) => item.teamId === (args.data as any).teamId,
      );
      (args.cell as HTMLElement).innerText = (index + 1).toString();
    }
  }

  onDataBound(): void {
    // Auto-fit all columns to content on data load
    this.grid?.autoFitColumns();
  }

  /**
   * Programmatically trigger Excel export; called from parent Payment step.
   */
  exportPaymentsToExcel(): void {
    const excelExportProperties = {
      dataSource: this.svc.lineItems(),
      fileName: 'TeamPaymentSummary.xlsx',
    };
    this.grid.excelExport(excelExportProperties);
  }
}
