import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorModule } from '@syncfusion/ej2-angular-richtexteditor';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import type { UpdateJobConfigPaymentRequest } from '@core/api';

@Component({
  selector: 'app-payment-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payment-tab.component.html',
})
export class PaymentTabComponent {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  // ── Local form model ──

  paymentMethodsAllowedCode = signal(1);
  bAddProcessingFees = signal(false);
  processingFeePercent = signal(0);
  bApplyProcessingFeesToTeamDeposit = signal<boolean | null>(null);
  perPlayerCharge = signal(0);
  perTeamCharge = signal(0);
  perMonthCharge = signal(0);
  payTo = signal<string | null>(null);
  mailTo = signal<string | null>(null);
  mailinPaymentWarning = signal<string | null>(null);
  balancedueaspercent = signal<string | null>(null);
  bTeamsFullPaymentRequired = signal<boolean | null>(null);
  bAllowRefundsInPriorMonths = signal<boolean | null>(null);
  bAllowCreditAll = signal<boolean | null>(null);

  // SuperUser-only
  adnArb = signal<boolean | null>(null);
  adnArbBillingOccurrences = signal<number | undefined>(undefined);
  adnArbIntervalLength = signal<number | undefined>(undefined);
  adnArbStartDate = signal<string | null>(null);
  adnArbMinimumTotalCharge = signal<number | undefined>(undefined);

  constructor() {
    effect(() => {
      const p = this.svc.payment();
      if (!p) return;
      this.paymentMethodsAllowedCode.set(p.paymentMethodsAllowedCode);
      this.bAddProcessingFees.set(p.bAddProcessingFees);
      this.processingFeePercent.set(p.processingFeePercent);
      this.bApplyProcessingFeesToTeamDeposit.set(p.bApplyProcessingFeesToTeamDeposit);
      this.perPlayerCharge.set(p.perPlayerCharge);
      this.perTeamCharge.set(p.perTeamCharge);
      this.perMonthCharge.set(p.perMonthCharge);
      this.payTo.set(p.payTo);
      this.mailTo.set(p.mailTo);
      this.mailinPaymentWarning.set(p.mailinPaymentWarning);
      this.balancedueaspercent.set(p.balancedueaspercent);
      this.bTeamsFullPaymentRequired.set(p.bTeamsFullPaymentRequired);
      this.bAllowRefundsInPriorMonths.set(p.bAllowRefundsInPriorMonths);
      this.bAllowCreditAll.set(p.bAllowCreditAll);
      this.adnArb.set(p.adnArb ?? null);
      this.adnArbBillingOccurrences.set(p.adnArbBillingOccurrences);
      this.adnArbIntervalLength.set(p.adnArbIntervalLength);
      this.adnArbStartDate.set(p.adnArbStartDate ?? null);
      this.adnArbMinimumTotalCharge.set(p.adnArbMinimumTotalCharge);
    });
  }

  onFieldChange(): void {
    this.svc.markDirty('payment');
  }

  onRteChange(field: string, event: any): void {
    const sig = (this as any)[field];
    if (sig?.set) sig.set(event.value ?? '');
    this.onFieldChange();
  }

  save(): void {
    const req: UpdateJobConfigPaymentRequest = {
      paymentMethodsAllowedCode: this.paymentMethodsAllowedCode(),
      bAddProcessingFees: this.bAddProcessingFees(),
      processingFeePercent: this.processingFeePercent(),
      bApplyProcessingFeesToTeamDeposit: this.bApplyProcessingFeesToTeamDeposit(),
      perPlayerCharge: this.perPlayerCharge(),
      perTeamCharge: this.perTeamCharge(),
      perMonthCharge: this.perMonthCharge(),
      payTo: this.payTo(),
      mailTo: this.mailTo(),
      mailinPaymentWarning: this.mailinPaymentWarning(),
      balancedueaspercent: this.balancedueaspercent(),
      bTeamsFullPaymentRequired: this.bTeamsFullPaymentRequired(),
      bAllowRefundsInPriorMonths: this.bAllowRefundsInPriorMonths(),
      bAllowCreditAll: this.bAllowCreditAll(),
    };
    if (this.svc.isSuperUser()) {
      req.adnArb = this.adnArb();
      req.adnArbBillingOccurrences = this.adnArbBillingOccurrences();
      req.adnArbIntervalLength = this.adnArbIntervalLength();
      req.adnArbStartDate = this.adnArbStartDate();
      req.adnArbMinimumTotalCharge = this.adnArbMinimumTotalCharge();
    }
    this.svc.savePayment(req);
  }
}
