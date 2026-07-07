import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import type { UpdateJobConfigCoachesRequest } from '@core/api';

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
  adultRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.adultRegConfirmationEmail ?? null);
  adultRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.adultRegConfirmationOnScreen ?? null);
  adultRegRefundPolicy = linkedSignal(() => this.svc.coaches()?.adultRegRefundPolicy ?? null);
  adultRegReleaseOfLiability = linkedSignal(() => this.svc.coaches()?.adultRegReleaseOfLiability ?? null);
  adultRegCodeOfConduct = linkedSignal(() => this.svc.coaches()?.adultRegCodeOfConduct ?? null);
  refereeRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationEmail ?? null);
  refereeRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationOnScreen ?? null);
  recruiterRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationEmail ?? null);
  recruiterRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationOnScreen ?? null);

  private readonly cleanSnapshot = computed(() => {
    const c = this.svc.coaches();
    if (!c) return '';
    return JSON.stringify({
      bRegistrationAllowStaff: c.bRegistrationAllowStaff,
      bRegistrationAllowReferee: c.bRegistrationAllowReferee,
      bRegistrationAllowRecruiter: c.bRegistrationAllowRecruiter,
      adultRegConfirmationEmail: c.adultRegConfirmationEmail,
      adultRegConfirmationOnScreen: c.adultRegConfirmationOnScreen,
      adultRegRefundPolicy: c.adultRegRefundPolicy,
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

  // Staged picker values — reseed from the server-derived identity whenever the config reloads.
  coachProfileCode = linkedSignal(() => this.svc.coaches()?.adultCoachProfileCode ?? '');
  coachRequiresUsLax = linkedSignal(() => this.svc.coaches()?.adultCoachRequiresUsLax ?? false);

  /** Whether the currently-selected profile supports the USA Lacrosse capability (AC3 does not). */
  readonly selectedProfileCanUsLax = computed(() =>
    this.availableCoachProfiles().find(p => p.code === this.coachProfileCode())?.canRequireUsLax ?? false);

  readonly selectedProfileName = computed(() =>
    this.availableCoachProfiles().find(p => p.code === this.coachProfileCode())?.name ?? this.coachProfileCode());

  /** True when the staged template differs from the job's current one — lights up the Apply button. */
  readonly coachFormDirty = computed(() => {
    const c = this.svc.coaches();
    if (!c) return false;
    return this.coachProfileCode() !== c.adultCoachProfileCode
      || (this.selectedProfileCanUsLax() && this.coachRequiresUsLax()) !== c.adultCoachRequiresUsLax;
  });

  showSwapConfirm = signal(false);

  onCoachProfileChange(code: string): void {
    this.coachProfileCode.set(code);
    // AC3 can't carry a USLax number — coerce the flag off so a stale check doesn't ride along.
    if (!this.selectedProfileCanUsLax()) this.coachRequiresUsLax.set(false);
  }

  readonly swapConfirmMessage = computed(() =>
    `Rebuild this job's coach form to <strong>${this.selectedProfileName()}</strong>`
    + `${this.effectiveRequiresUsLax() ? ' <em>(with USA Lacrosse number)</em>' : ''}?`
    + `<br><br>This replaces any custom-added fields on the coach form. `
    + `Referee and Recruiter forms are unaffected.`);

  private effectiveRequiresUsLax(): boolean {
    return this.selectedProfileCanUsLax() && this.coachRequiresUsLax();
  }

  requestSwap(): void {
    if (this.coachFormDirty()) this.showSwapConfirm.set(true);
  }

  confirmSwap(): void {
    this.showSwapConfirm.set(false);
    this.svc.swapCoachFormTemplate({
      profileCode: this.coachProfileCode(),
      requiresUsLax: this.effectiveRequiresUsLax(),
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
      adultRegConfirmationEmail: this.adultRegConfirmationEmail(),
      adultRegConfirmationOnScreen: this.adultRegConfirmationOnScreen(),
      adultRegRefundPolicy: this.adultRegRefundPolicy(),
      adultRegReleaseOfLiability: this.adultRegReleaseOfLiability(),
      adultRegCodeOfConduct: this.adultRegCodeOfConduct(),
      refereeRegConfirmationEmail: this.refereeRegConfirmationEmail(),
      refereeRegConfirmationOnScreen: this.refereeRegConfirmationOnScreen(),
      recruiterRegConfirmationEmail: this.recruiterRegConfirmationEmail(),
      recruiterRegConfirmationOnScreen: this.recruiterRegConfirmationOnScreen(),
    };
  }
}
