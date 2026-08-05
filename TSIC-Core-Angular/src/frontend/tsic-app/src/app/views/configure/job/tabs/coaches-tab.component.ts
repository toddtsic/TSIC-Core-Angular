import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import type { UpdateJobConfigCoachesRequest, AdultUsLaxMode } from '@core/api';

@Component({
  selector: 'app-coaches-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './coaches-tab.component.html',
})
export class CoachesTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  bRegistrationAllowStaff = linkedSignal(() => this.svc.coaches()?.bRegistrationAllowStaff ?? null);
  bRegistrationAllowReferee = linkedSignal(() => this.svc.coaches()?.bRegistrationAllowReferee ?? null);
  bRegistrationAllowRecruiter = linkedSignal(() => this.svc.coaches()?.bRegistrationAllowRecruiter ?? null);
  // CoachReg pair = coach persona; club-rep confirmations live on the Teams tab.
  coachRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.coachRegConfirmationEmail ?? null);
  coachRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.coachRegConfirmationOnScreen ?? null);
  // No adult refund policy editor: AdultReg_RefundPolicy is a dead column (adult flows are
  // unpaid). The job-wide refund policy is edited on the Payment tab.
  adultRegReleaseOfLiability = linkedSignal(() => this.svc.coaches()?.adultRegReleaseOfLiability ?? null);
  adultRegCodeOfConduct = linkedSignal(() => this.svc.coaches()?.adultRegCodeOfConduct ?? null);
  refereeRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationEmail ?? null);
  refereeRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationOnScreen ?? null);
  recruiterRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationEmail ?? null);
  recruiterRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationOnScreen ?? null);

  // ── Section disclosure ──
  // Every section always starts open. AM-065 — a director must be able to prep coach,
  // referee, recruiter and adult-waiver copy before releasing registration, so the
  // editors' presence cannot depend on the Registration Availability toggles.
  // These were linkedSignals seeded from those toggles; because each section renders its
  // closed-note *instead of* the editors, a job with those flows off showed no text box
  // at all until you clicked the header. Plain signals, not linkedSignals, also remove
  // the reseed-on-toggle path that tore down the ej2 RTE mid-edit (ej2 change fires on
  // blur, so copy typed and not yet blurred was lost when a toggle flipped).
  // The header toggle still works — the template already does .set(!x()).
  coachOpen = signal(true);
  refereeOpen = signal(true);
  recruiterOpen = signal(true);
  waiversOpen = signal(true);

  private readonly cleanSnapshot = computed(() => {
    const c = this.svc.coaches();
    if (!c) return '';
    return JSON.stringify({
      bRegistrationAllowStaff: c.bRegistrationAllowStaff,
      bRegistrationAllowReferee: c.bRegistrationAllowReferee,
      bRegistrationAllowRecruiter: c.bRegistrationAllowRecruiter,
      coachRegConfirmationEmail: c.coachRegConfirmationEmail,
      coachRegConfirmationOnScreen: c.coachRegConfirmationOnScreen,
      adultRegReleaseOfLiability: c.adultRegReleaseOfLiability,
      adultRegCodeOfConduct: c.adultRegCodeOfConduct,
      refereeRegConfirmationEmail: c.refereeRegConfirmationEmail,
      refereeRegConfirmationOnScreen: c.refereeRegConfirmationOnScreen,
      recruiterRegConfirmationEmail: c.recruiterRegConfirmationEmail,
      recruiterRegConfirmationOnScreen: c.recruiterRegConfirmationOnScreen,
    } satisfies UpdateJobConfigCoachesRequest);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
      this.svc.markClean('coaches');
    } else {
      this.svc.markDirty('coaches');
    }
  }

  onRteChange(field: string, event: any): void {
    const sig = (this as any)[field];
    if (sig?.set) sig.set(event.value ?? '');
    this.onFieldChange();
  }

  // ── Coach-form template picker (SuperUser only; a distinct confirmed action, not the batched save) ──

  readonly availableCoachProfiles = computed(() => this.svc.coaches()?.availableAdultCoachProfiles ?? []);

  /**
   * The three USA Lacrosse states. AdultUsLaxMode generates as a bare `number`, so the values are named
   * here rather than scattered as literals. Optional is the safe middle: the field is collected and a
   * SUPPLIED number is still hard-validated against USA Lacrosse, but a coach without one is never
   * blocked from registering.
   */
  readonly usLaxOptions: { value: AdultUsLaxMode; label: string; hint: string }[] = [
    { value: 0, label: 'Not collected', hint: 'No USA Lacrosse field on the coach form.' },
    {
      value: 1, label: 'Collected — not required',
      hint: 'Shown but optional. A number that is entered is still verified; leaving it blank never blocks registration.',
    },
    {
      value: 2, label: 'Required',
      hint: 'A coach cannot complete registration without an active USA Lacrosse membership.',
    },
  ];

  // Staged picker values — reseed from the server-derived identity whenever the config reloads.
  coachProfileCode = linkedSignal(() => this.svc.coaches()?.adultCoachProfileCode ?? '');
  coachUsLax = linkedSignal<AdultUsLaxMode>(() => this.svc.coaches()?.adultCoachUsLax ?? 0);

  readonly selectedUsLaxHint = computed(() =>
    this.usLaxOptions.find(o => o.value === this.coachUsLax())?.hint ?? '');

  readonly selectedProfileName = computed(() =>
    this.availableCoachProfiles().find(p => p.code === this.coachProfileCode())?.name ?? this.coachProfileCode());

  /** True when the staged template differs from the job's current one — lights up the Apply button. */
  readonly coachFormDirty = computed(() => {
    const c = this.svc.coaches();
    if (!c) return false;
    return this.coachProfileCode() !== c.adultCoachProfileCode
      || this.coachUsLax() !== c.adultCoachUsLax;
  });

  showSwapConfirm = signal(false);

  onCoachProfileChange(code: string): void {
    // No coercion: USLax now rides on a RegformName_Coach pipe token rather than on the choice of
    // legacy form name, so every profile — AC3 included — can carry it.
    this.coachProfileCode.set(code);
  }

  readonly swapConfirmMessage = computed(() => {
    const usLax = this.usLaxOptions.find(o => o.value === this.coachUsLax());
    return `Rebuild this job's coach form to <strong>${this.selectedProfileName()}</strong>`
      + `, USA Lacrosse number <strong>${(usLax?.label ?? '').toLowerCase()}</strong>?`
      + `<br><br>This replaces any custom-added fields on the coach form. `
      + `Referee and Recruiter forms are unaffected.`;
  });

  requestSwap(): void {
    if (this.coachFormDirty()) this.showSwapConfirm.set(true);
  }

  confirmSwap(): void {
    this.showSwapConfirm.set(false);
    this.svc.swapCoachFormTemplate({
      profileCode: this.coachProfileCode(),
      usLax: this.coachUsLax(),
    });
  }

  cancelSwap(): void {
    this.showSwapConfirm.set(false);
  }

  save(): void {
    this.svc.saveCoaches(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigCoachesRequest {
    return {
      bRegistrationAllowStaff: this.bRegistrationAllowStaff(),
      bRegistrationAllowReferee: this.bRegistrationAllowReferee(),
      bRegistrationAllowRecruiter: this.bRegistrationAllowRecruiter(),
      coachRegConfirmationEmail: this.coachRegConfirmationEmail(),
      coachRegConfirmationOnScreen: this.coachRegConfirmationOnScreen(),
      adultRegReleaseOfLiability: this.adultRegReleaseOfLiability(),
      adultRegCodeOfConduct: this.adultRegCodeOfConduct(),
      refereeRegConfirmationEmail: this.refereeRegConfirmationEmail(),
      refereeRegConfirmationOnScreen: this.refereeRegConfirmationOnScreen(),
      recruiterRegConfirmationEmail: this.recruiterRegConfirmationEmail(),
      recruiterRegConfirmationOnScreen: this.recruiterRegConfirmationOnScreen(),
    };
  }
}
