import { Component, inject, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TeamPaymentService } from '../services/team-payment.service';
import { GridModule, GridComponent, QueryCellInfoEventArgs } from '@syncfusion/ej2-angular-grids';

/**
 * Team payment summary grid - displays registered teams with fees and balances using Syncfusion Grid.
 * Matches production jqGrid layout with row numbers and aggregate footer.
 */
@Component({
  selector: 'app-team-payment-summary-table',
  standalone: true,
  imports: [CommonModule, GridModule],
  template: `
    <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="team-pay-summary-title"
             style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
      <h6 id="team-pay-summary-title" class="fw-semibold mb-2">Team Payment Summary</h6>
      
      <!-- Payment Method Selector (show when both CC and Check are allowed) -->
      @if (svc.showPaymentMethodSelector()) {
        <div class="mb-3">
          <label for="paymentMethod" class="form-label fw-semibold">How will you be paying?</label>
          <select id="paymentMethod" class="form-select" 
                  [value]="svc.selectedPaymentMethod()"
                  (change)="svc.selectedPaymentMethod.set($any($event.target).value)">
            <option value="CC">Credit Card</option>
            <option value="Check">Check</option>
          </select>
          @if (svc.selectedPaymentMethod() === 'Check') {
            <div class="form-text">
              <i class="bi bi-info-circle me-1"></i>
              Processing fees are waived for check payments. 
              You save {{ svc.processingFeeSavings() | currency }}!
            </div>
          }
        </div>
      }

      <div class="grid-wrapper" style="width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch;">
        <ejs-grid #grid [dataSource]="svc.lineItems()" [allowSorting]="true"
                  height="auto" [enableHover]="true" [enableAltRow]="true" 
                  [rowHeight]="32" gridLines="Both"
                  (queryCellInfo)="onQueryCellInfo($event)"
                  style="min-width: 700px;">
          <e-columns>
            <!-- Row Number Column -->
            <e-column field="rowNum" headerText="#" width="60" textAlign="Center" 
                      [allowSorting]="false"></e-column>

            <!-- Age Group Column -->
            <e-column field="ageGroup" headerText="Age Group" width="120"></e-column>

            <!-- Team Name Column -->
            <e-column field="teamName" headerText="Team" width="150"></e-column>

            <!-- Level of Play Column -->
            <e-column field="levelOfPlay" headerText="LOP" width="100" textAlign="Center">
              <ng-template #template let-data>
                {{ data.levelOfPlay || '' }}
              </ng-template>
            </e-column>

            <!-- Registration Date Column -->
            <e-column field="registrationTs" headerText="Reg-Date" width="110" 
                      textAlign="Center" type="date" format="MM/dd/yyyy"></e-column>

            <!-- Paid Total Column -->
            <e-column field="paidTotal" headerText="Paid Total" width="110" 
                      textAlign="Right" format="C2"></e-column>

            <!-- Deposit Due Column -->
            <e-column field="depositDue" headerText="Deposit Due" width="120" 
                      textAlign="Right" format="C2"></e-column>

            <!-- Additional Due Column -->
            <e-column field="additionalDue" headerText="Additional Due" width="130" 
                      textAlign="Right" format="C2"></e-column>

            <!-- Fee Processing Column (conditional - hidden when !bAddProcessingFees) -->
            @if (svc.showFeeProcessingColumn()) {
              <e-column field="feeProcessing" headerText="Fee-Processing" width="130" 
                        textAlign="Right" format="C2"></e-column>
            }

            <!-- CC Owed Total Column (always visible) -->
            <e-column field="ccOwedTotal" headerText="CC Owed Total" width="130" 
                      textAlign="Right" format="C2"></e-column>

            <!-- Ck Owed Total Column (conditional - hidden when !bAddProcessingFees) -->
            @if (svc.showFeeProcessingColumn()) {
              <e-column field="ckOwedTotal" headerText="Ck Owed Total" width="130" 
                        textAlign="Right" format="C2"></e-column>
            }
          </e-columns>

          <!-- Aggregates for Footer Totals -->
          <e-aggregates>
            <e-aggregate>
              <e-columns>
                <e-column field="rowNum" type="Custom" [footerTemplate]="emptyTemplate"></e-column>
                <e-column field="ageGroup" type="Custom" [footerTemplate]="emptyTemplate"></e-column>
                <e-column field="teamName" type="Custom" [footerTemplate]="emptyTemplate"></e-column>
                <e-column field="levelOfPlay" type="Custom" [footerTemplate]="emptyTemplate"></e-column>
                <e-column field="registrationTs" type="Custom" [footerTemplate]="totalsLabelTemplate"></e-column>
                <e-column field="paidTotal" type="Sum" [footerTemplate]="paidTotalTemplate"></e-column>
                <e-column field="depositDue" type="Sum" [footerTemplate]="depositDueTemplate"></e-column>
                <e-column field="additionalDue" type="Sum" [footerTemplate]="additionalDueTemplate"></e-column>
                @if (svc.showFeeProcessingColumn()) {
                  <e-column field="feeProcessing" type="Sum" [footerTemplate]="feeProcessingTemplate"></e-column>
                }
                <e-column field="ccOwedTotal" type="Sum" [footerTemplate]="ccOwedTemplate"></e-column>
                @if (svc.showFeeProcessingColumn()) {
                  <e-column field="ckOwedTotal" type="Sum" [footerTemplate]="ckOwedTemplate"></e-column>
                }
              </e-columns>
            </e-aggregate>
          </e-aggregates>
        </ejs-grid>
      </div>

      <!-- Footer Templates -->
      <ng-template #emptyTemplate let-data>
        <span></span>
      </ng-template>
      <ng-template #totalsLabelTemplate let-data>
        <span class="fw-bold">Totals</span>
      </ng-template>
      <ng-template #paidTotalTemplate let-data>
        <span class="fw-bold text-success">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #depositDueTemplate let-data>
        <span class="fw-bold" [class.text-warning]="data.Sum > 0" 
              [class.text-muted]="data.Sum === 0">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #additionalDueTemplate let-data>
        <span class="fw-bold" [class.text-warning]="data.Sum > 0" 
              [class.text-muted]="data.Sum === 0">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #feeProcessingTemplate let-data>
        <span class="fw-bold text-muted">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #ccOwedTemplate let-data>
        <span class="fw-bold" [class.text-warning]="data.Sum > 0" 
              [class.text-muted]="data.Sum === 0">{{ data.Sum | currency }}</span>
      </ng-template>
      <ng-template #ckOwedTemplate let-data>
        <span class="fw-bold" [class.text-warning]="data.Sum > 0" 
              [class.text-muted]="data.Sum === 0">{{ data.Sum | currency }}</span>
      </ng-template>
      
      @if (!svc.hasBalance()) {
        <div class="alert alert-success border-0 mt-3 mb-0" role="status">
          <i class="bi bi-check-circle-fill me-2"></i>
          All teams are fully paid. No payment is required.
        </div>
      }
    </section>
  `,
  styles: [`
    /* Desktop: Tighter density to match production */
    ::ng-deep .e-grid .e-headercell {
        font-size: 0.875rem;
        padding: 6px 8px;
        line-height: 1.3;
    }
    ::ng-deep .e-grid .e-headercelldiv {
        font-size: 0.875rem;
        line-height: 1.3;
    }
    ::ng-deep .e-grid .e-gridcontent td {
        font-size: 0.875rem;
        padding: 6px 8px;
        line-height: 1.3;
    }

    /* Mobile: smaller font and compact layout */
    @media (max-width: 767.98px) {
        ::ng-deep .e-grid .e-headercell {
            font-size: 0.65rem;
            padding: 2px 4px;
            white-space: normal;
            word-wrap: break-word;
            line-height: 1.1;
        }
        ::ng-deep .e-grid .e-headercelldiv {
            font-size: 0.65rem;
            line-height: 1.1;
        }
        ::ng-deep .e-grid .e-gridcontent td {
            font-size: 0.75rem;
            padding: 2px 4px;
            line-height: 1.1;
        }
        ::ng-deep .e-grid colgroup col {
            min-width: 45px !important;
        }
    }
  `]
})
export class TeamPaymentSummaryTableComponent {
  readonly svc = inject(TeamPaymentService);

  @ViewChild('grid') public grid!: GridComponent;

  onQueryCellInfo(args: QueryCellInfoEventArgs): void {
    if (args.column?.field === 'rowNum' && args.data) {
      const index = (this.grid.currentViewData as any[]).findIndex(
        item => item.teamId === (args.data as any).teamId
      );
      (args.cell as HTMLElement).innerText = (index + 1).toString();
    }
  }
}
