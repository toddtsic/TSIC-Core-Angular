import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import type { UpdateJobConfigCoachesRequest } from '@core/api';

@Component({
  selector: 'app-coaches-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './coaches-tab.component.html',
})
export class CoachesTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  regformNameCoach = linkedSignal(() => this.svc.coaches()?.regformNameCoach ?? '');
  adultRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.adultRegConfirmationEmail ?? null);
  adultRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.adultRegConfirmationOnScreen ?? null);
  adultRegRefundPolicy = linkedSignal(() => this.svc.coaches()?.adultRegRefundPolicy ?? null);
  adultRegReleaseOfLiability = linkedSignal(() => this.svc.coaches()?.adultRegReleaseOfLiability ?? null);
  adultRegCodeOfConduct = linkedSignal(() => this.svc.coaches()?.adultRegCodeOfConduct ?? null);
  refereeRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationEmail ?? null);
  refereeRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.refereeRegConfirmationOnScreen ?? null);
  recruiterRegConfirmationEmail = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationEmail ?? null);
  recruiterRegConfirmationOnScreen = linkedSignal(() => this.svc.coaches()?.recruiterRegConfirmationOnScreen ?? null);
  bAllowRosterViewAdult = linkedSignal(() => this.svc.coaches()?.bAllowRosterViewAdult ?? false);
  bAllowRosterViewPlayer = linkedSignal(() => this.svc.coaches()?.bAllowRosterViewPlayer ?? false);

  private readonly cleanSnapshot = computed(() => {
    const c = this.svc.coaches();
    if (!c) return '';
    return JSON.stringify({
      regformNameCoach: c.regformNameCoach,
      adultRegConfirmationEmail: c.adultRegConfirmationEmail,
      adultRegConfirmationOnScreen: c.adultRegConfirmationOnScreen,
      adultRegRefundPolicy: c.adultRegRefundPolicy,
      adultRegReleaseOfLiability: c.adultRegReleaseOfLiability,
      adultRegCodeOfConduct: c.adultRegCodeOfConduct,
      refereeRegConfirmationEmail: c.refereeRegConfirmationEmail,
      refereeRegConfirmationOnScreen: c.refereeRegConfirmationOnScreen,
      recruiterRegConfirmationEmail: c.recruiterRegConfirmationEmail,
      recruiterRegConfirmationOnScreen: c.recruiterRegConfirmationOnScreen,
      bAllowRosterViewAdult: c.bAllowRosterViewAdult,
      bAllowRosterViewPlayer: c.bAllowRosterViewPlayer,
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

  save(): void {
    this.svc.saveCoaches(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigCoachesRequest {
    return {
      regformNameCoach: this.regformNameCoach(),
      adultRegConfirmationEmail: this.adultRegConfirmationEmail(),
      adultRegConfirmationOnScreen: this.adultRegConfirmationOnScreen(),
      adultRegRefundPolicy: this.adultRegRefundPolicy(),
      adultRegReleaseOfLiability: this.adultRegReleaseOfLiability(),
      adultRegCodeOfConduct: this.adultRegCodeOfConduct(),
      refereeRegConfirmationEmail: this.refereeRegConfirmationEmail(),
      refereeRegConfirmationOnScreen: this.refereeRegConfirmationOnScreen(),
      recruiterRegConfirmationEmail: this.recruiterRegConfirmationEmail(),
      recruiterRegConfirmationOnScreen: this.recruiterRegConfirmationOnScreen(),
      bAllowRosterViewAdult: this.bAllowRosterViewAdult(),
      bAllowRosterViewPlayer: this.bAllowRosterViewPlayer(),
    };
  }
}
