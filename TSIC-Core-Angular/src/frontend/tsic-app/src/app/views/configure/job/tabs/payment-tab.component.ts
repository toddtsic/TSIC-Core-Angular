import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import { toDateOnly } from '../shared/rte-config';
import type { UpdateJobConfigPaymentRequest } from '@core/api';

@Component({
  selector: 'app-payment-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './payment-tab.component.html',
})
export class PaymentTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  // ── Local form model ──

  paymentMethodsAllowedCode = linkedSignal(() => this.svc.payment()?.paymentMethodsAllowedCode ?? 1);
  bAddProcessingFees = linkedSignal(() => this.svc.payment()?.bAddProcessingFees ?? false);
  processingFeePercent = linkedSignal(() => this.svc.payment()?.processingFeePercent ?? null);
  bEnableEcheck = linkedSignal(() => this.svc.payment()?.bEnableEcheck ?? false);
  ecprocessingFeePercent = linkedSignal(() => this.svc.payment()?.ecprocessingFeePercent ?? null);
  minProcessingFeePercent = computed(() => this.svc.payment()?.minProcessingFeePercent ?? null);
  maxProcessingFeePercent = computed(() => this.svc.payment()?.maxProcessingFeePercent ?? null);
  minEcprocessingFeePercent = computed(() => this.svc.payment()?.minEcprocessingFeePercent ?? null);
  maxEcprocessingFeePercent = computed(() => this.svc.payment()?.maxEcprocessingFeePercent ?? null);
  /** eCheck checkbox is only enabled when check is allowed (code 2 or 3). */
  echeckCheckboxAllowed = computed(() => {
    const code = this.paymentMethodsAllowedCode();
    return code === 2 || code === 3;
  });
  bApplyProcessingFeesToTeamDeposit = linkedSignal(() => this.svc.payment()?.bApplyProcessingFeesToTeamDeposit ?? null);
  perPlayerCharge = linkedSignal(() => this.svc.payment()?.perPlayerCharge ?? null);
  perTeamCharge = linkedSignal(() => this.svc.payment()?.perTeamCharge ?? null);
  perMonthCharge = linkedSignal(() => this.svc.payment()?.perMonthCharge ?? null);
  payTo = linkedSignal(() => this.svc.payment()?.payTo ?? null);
  mailTo = linkedSignal(() => this.svc.payment()?.mailTo ?? null);
  mailinPaymentWarning = linkedSignal(() => this.svc.payment()?.mailinPaymentWarning ?? null);
  balancedueaspercent = linkedSignal(() => this.svc.payment()?.balancedueaspercent ?? null);
  bTeamsFullPaymentRequired = linkedSignal(() => this.svc.payment()?.bTeamsFullPaymentRequired ?? null);
  bPlayersFullPaymentRequired = linkedSignal(() => this.svc.payment()?.bPlayersFullPaymentRequired ?? false);
  bAllowRefundsInPriorMonths = linkedSignal(() => this.svc.payment()?.bAllowRefundsInPriorMonths ?? null);
  bAllowCreditAll = linkedSignal(() => this.svc.payment()?.bAllowCreditAll ?? null);

  // SuperUser-only
  adnArb = linkedSignal(() => this.svc.payment()?.adnArb ?? null);
  adnArbBillingOccurrences = linkedSignal(() => this.svc.payment()?.adnArbBillingOccurrences);
  adnArbIntervalLength = linkedSignal(() => this.svc.payment()?.adnArbIntervalLength);
  adnArbStartDate = linkedSignal(() => toDateOnly(this.svc.payment()?.adnArbStartDate) ?? null);
  adnArbMinimumTotalCharge = linkedSignal(() => this.svc.payment()?.adnArbMinimumTotalCharge);

  private readonly cleanSnapshot = computed(() => {
    const p = this.svc.payment();
    if (!p) return '';
    const req: UpdateJobConfigPaymentRequest = {
      paymentMethodsAllowedCode: p.paymentMethodsAllowedCode,
      bAddProcessingFees: p.bAddProcessingFees,
      processingFeePercent: p.processingFeePercent,
      bEnableEcheck: p.bEnableEcheck,
      ecprocessingFeePercent: p.ecprocessingFeePercent,
      bApplyProcessingFeesToTeamDeposit: p.bApplyProcessingFeesToTeamDeposit,
      perPlayerCharge: p.perPlayerCharge,
      perTeamCharge: p.perTeamCharge,
      perMonthCharge: p.perMonthCharge,
      payTo: p.payTo,
      mailTo: p.mailTo,
      mailinPaymentWarning: p.mailinPaymentWarning,
      balancedueaspercent: p.balancedueaspercent,
      bTeamsFullPaymentRequired: p.bTeamsFullPaymentRequired,
      bPlayersFullPaymentRequired: p.bPlayersFullPaymentRequired,
      bAllowRefundsInPriorMonths: p.bAllowRefundsInPriorMonths,
      bAllowCreditAll: p.bAllowCreditAll,
    };
    if (this.svc.isSuperUser()) {
      req.adnArb = p.adnArb ?? null;
      req.adnArbBillingOccurrences = p.adnArbBillingOccurrences;
      req.adnArbIntervalLength = p.adnArbIntervalLength;
      req.adnArbStartDate = toDateOnly(p.adnArbStartDate) ?? null;
      req.adnArbMinimumTotalCharge = p.adnArbMinimumTotalCharge;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
      this.svc.markClean('payment');
    } else {
      this.svc.markDirty('payment');
    }
  }

  /**
   * Handles Allowed Methods radio change. Auto-clears bEnableEcheck when code
   * transitions to 1 (CC only) — eCheck depends on check being enabled.
   */
  setPaymentMethodsAllowedCode(code: number): void {
    this.paymentMethodsAllowedCode.set(code);
    if (code === 1 && this.bEnableEcheck()) {
      this.bEnableEcheck.set(false);
    }
    this.onFieldChange();
  }

  onProcessingFeeChange(value: number | null): void {
    this.processingFeePercent.set(this.clampPercent(value, this.minProcessingFeePercent(), this.maxProcessingFeePercent()));
    this.onFieldChange();
  }

  onEcprocessingFeeChange(value: number | null): void {
    this.ecprocessingFeePercent.set(this.clampPercent(value, this.minEcprocessingFeePercent(), this.maxEcprocessingFeePercent()));
    this.onFieldChange();
  }

  private clampPercent(value: number | null, min: number | null, max: number | null): number | null {
    if (value === null) return null;
    if (min !== null && value < min) return min;
    if (max !== null && value > max) return max;
    return value;
  }

  save(): void {
    this.svc.savePayment(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigPaymentRequest {
    const req: UpdateJobConfigPaymentRequest = {
      paymentMethodsAllowedCode: this.paymentMethodsAllowedCode(),
      bAddProcessingFees: this.bAddProcessingFees(),
      processingFeePercent: this.processingFeePercent(),
      bEnableEcheck: this.bEnableEcheck(),
      ecprocessingFeePercent: this.ecprocessingFeePercent(),
      bApplyProcessingFeesToTeamDeposit: this.bApplyProcessingFeesToTeamDeposit(),
      perPlayerCharge: this.perPlayerCharge(),
      perTeamCharge: this.perTeamCharge(),
      perMonthCharge: this.perMonthCharge(),
      payTo: this.payTo(),
      mailTo: this.mailTo(),
      mailinPaymentWarning: this.mailinPaymentWarning(),
      balancedueaspercent: this.balancedueaspercent(),
      bTeamsFullPaymentRequired: this.bTeamsFullPaymentRequired(),
      bPlayersFullPaymentRequired: this.bPlayersFullPaymentRequired(),
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
    return req;
  }
}
