import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import { toDateOnly } from '../shared/rte-config';
import type {
  UpdateJobConfigPaymentRequest,
  CreateAdminChargeRequest,
  UpdateAdminChargeRequest,
  JobAdminChargeDto,
} from '@core/api';

/** Editable shape for the admin-charge add/edit rows (SuperUser only). */
interface AdminChargeDraft {
  chargeTypeId: number | null;
  chargeAmount: number | null;
  year: number;
  month: number;
  comment: string | null;
}

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
  payTo = linkedSignal(() => this.svc.payment()?.payTo ?? null);
  mailTo = linkedSignal(() => this.svc.payment()?.mailTo ?? null);
  mailinPaymentWarning = linkedSignal(() => this.svc.payment()?.mailinPaymentWarning ?? null);
  balancedueaspercent = linkedSignal(() => this.svc.payment()?.balancedueaspercent ?? null);
  bTeamsFullPaymentRequired = linkedSignal(() => this.svc.payment()?.bTeamsFullPaymentRequired ?? null);
  bPlayersFullPaymentRequired = linkedSignal(() => this.svc.payment()?.bPlayersFullPaymentRequired ?? false);
  bIncludePlayerDonation = linkedSignal(() => this.svc.payment()?.bIncludePlayerDonation ?? false);
  bIncludeTeamDonation = linkedSignal(() => this.svc.payment()?.bIncludeTeamDonation ?? false);
  bAllowRefundsInPriorMonths = linkedSignal(() => this.svc.payment()?.bAllowRefundsInPriorMonths ?? null);
  bAllowCreditAll = linkedSignal(() => this.svc.payment()?.bAllowCreditAll ?? null);

  // SuperUser-only
  perPlayerCharge = linkedSignal(() => this.svc.payment()?.perPlayerCharge ?? null);
  perTeamCharge = linkedSignal(() => this.svc.payment()?.perTeamCharge ?? null);
  perMonthCharge = linkedSignal(() => this.svc.payment()?.perMonthCharge ?? null);
  adnArb = linkedSignal(() => this.svc.payment()?.adnArb ?? null);
  adnArbBillingOccurrences = linkedSignal(() => this.svc.payment()?.adnArbBillingOccurrences);
  adnArbIntervalLength = linkedSignal(() => this.svc.payment()?.adnArbIntervalLength);
  adnArbStartDate = linkedSignal(() => toDateOnly(this.svc.payment()?.adnArbStartDate) ?? null);
  adnArbMinimumTotalCharge = linkedSignal(() => this.svc.payment()?.adnArbMinimumTotalCharge);
  adnArbTrial = linkedSignal(() => this.svc.payment()?.adnArbTrial ?? null);
  adnStartDateAfterTrial = linkedSignal(() => toDateOnly(this.svc.payment()?.adnStartDateAfterTrial) ?? null);

  /** ARB owns the balance schedule when either flag is on — Full-Payment-Required toggles are inert. */
  protected readonly arbControlsBalance = computed(() => !!this.adnArb() || !!this.adnArbTrial());

  // ── Admin Charges (SuperUser only) ────────────────────
  // A self-contained CRUD island: add/edit/delete are immediate HTTP actions on the service that
  // reload the config — they do NOT participate in the payment tab's dirty/batched-save flow.

  /** Charge-type dropdown options (reference data loaded by the shell at init). */
  protected readonly chargeTypes = computed(() => this.svc.referenceData()?.chargeTypes ?? []);

  /** Year picker: current + three prior years (matches legacy's this/last-year constraint, loosened slightly). */
  protected readonly yearOptions = ((): number[] => {
    const now = new Date().getFullYear();
    return [now, now - 1, now - 2, now - 3];
  })();

  protected readonly monthOptions = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

  /** id of the row currently in inline-edit mode, or null when none. */
  protected readonly editingChargeId = signal<number | null>(null);
  protected readonly editDraft = signal<AdminChargeDraft>(this.blankDraft());
  protected readonly newDraft = signal<AdminChargeDraft>(this.blankDraft());

  private blankDraft(): AdminChargeDraft {
    const now = new Date();
    return { chargeTypeId: null, chargeAmount: null, year: now.getFullYear(), month: now.getMonth() + 1, comment: null };
  }

  private draftValid(d: AdminChargeDraft): boolean {
    return d.chargeTypeId != null
      && d.chargeAmount != null && Number.isFinite(d.chargeAmount)
      && d.year != null && d.month != null;
  }

  protected readonly editDraftValid = computed(() => this.draftValid(this.editDraft()));
  protected readonly newDraftValid = computed(() => this.draftValid(this.newDraft()));

  patchNew(partial: Partial<AdminChargeDraft>): void {
    this.newDraft.set({ ...this.newDraft(), ...partial });
  }

  patchEdit(partial: Partial<AdminChargeDraft>): void {
    this.editDraft.set({ ...this.editDraft(), ...partial });
  }

  startEdit(charge: JobAdminChargeDto): void {
    this.editDraft.set({
      chargeTypeId: charge.chargeTypeId,
      chargeAmount: charge.chargeAmount,
      year: charge.year,
      month: charge.month,
      comment: charge.comment,
    });
    this.editingChargeId.set(charge.id);
  }

  cancelEdit(): void {
    this.editingChargeId.set(null);
  }

  saveEdit(): void {
    const id = this.editingChargeId();
    const d = this.editDraft();
    if (id == null || !this.draftValid(d)) return;
    const req: UpdateAdminChargeRequest = {
      chargeTypeId: d.chargeTypeId!,
      chargeAmount: d.chargeAmount!,
      comment: d.comment,
      year: d.year,
      month: d.month,
    };
    this.svc.updateAdminCharge(id, req);
    this.editingChargeId.set(null); // config reload repaints the row
  }

  addCharge(): void {
    const d = this.newDraft();
    if (!this.draftValid(d)) return;
    const req: CreateAdminChargeRequest = {
      chargeTypeId: d.chargeTypeId!,
      chargeAmount: d.chargeAmount!,
      comment: d.comment,
      year: d.year,
      month: d.month,
    };
    this.svc.addAdminCharge(req);
    this.newDraft.set(this.blankDraft()); // clear the add row
  }

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
      payTo: p.payTo,
      mailTo: p.mailTo,
      mailinPaymentWarning: p.mailinPaymentWarning,
      balancedueaspercent: p.balancedueaspercent,
      bTeamsFullPaymentRequired: p.bTeamsFullPaymentRequired,
      bPlayersFullPaymentRequired: p.bPlayersFullPaymentRequired,
      bIncludePlayerDonation: p.bIncludePlayerDonation,
      bIncludeTeamDonation: p.bIncludeTeamDonation,
      bAllowRefundsInPriorMonths: p.bAllowRefundsInPriorMonths,
      bAllowCreditAll: p.bAllowCreditAll,
    };
    if (this.svc.isSuperUser()) {
      req.perPlayerCharge = p.perPlayerCharge ?? null;
      req.perTeamCharge = p.perTeamCharge ?? null;
      req.perMonthCharge = p.perMonthCharge ?? null;
      req.adnArb = p.adnArb ?? null;
      req.adnArbBillingOccurrences = p.adnArbBillingOccurrences;
      req.adnArbIntervalLength = p.adnArbIntervalLength;
      req.adnArbStartDate = toDateOnly(p.adnArbStartDate) ?? null;
      req.adnArbMinimumTotalCharge = p.adnArbMinimumTotalCharge;
      req.adnArbTrial = p.adnArbTrial ?? null;
      req.adnStartDateAfterTrial = toDateOnly(p.adnStartDateAfterTrial) ?? null;
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
      payTo: this.payTo(),
      mailTo: this.mailTo(),
      mailinPaymentWarning: this.mailinPaymentWarning(),
      balancedueaspercent: this.balancedueaspercent(),
      bTeamsFullPaymentRequired: this.bTeamsFullPaymentRequired(),
      bPlayersFullPaymentRequired: this.bPlayersFullPaymentRequired(),
      bIncludePlayerDonation: this.bIncludePlayerDonation(),
      bIncludeTeamDonation: this.bIncludeTeamDonation(),
      bAllowRefundsInPriorMonths: this.bAllowRefundsInPriorMonths(),
      bAllowCreditAll: this.bAllowCreditAll(),
    };
    if (this.svc.isSuperUser()) {
      req.perPlayerCharge = this.perPlayerCharge();
      req.perTeamCharge = this.perTeamCharge();
      req.perMonthCharge = this.perMonthCharge();
      req.adnArb = this.adnArb();
      req.adnArbBillingOccurrences = this.adnArbBillingOccurrences();
      req.adnArbIntervalLength = this.adnArbIntervalLength();
      req.adnArbStartDate = this.adnArbStartDate();
      req.adnArbMinimumTotalCharge = this.adnArbMinimumTotalCharge();
      req.adnArbTrial = this.adnArbTrial();
      req.adnStartDateAfterTrial = this.adnStartDateAfterTrial();
    }
    return req;
  }
}
