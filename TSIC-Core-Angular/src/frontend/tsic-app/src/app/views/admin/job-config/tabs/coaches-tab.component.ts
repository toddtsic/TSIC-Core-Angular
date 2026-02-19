import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
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
export class CoachesTabComponent {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  regformNameCoach = signal('');
  adultRegConfirmationEmail = signal<string | null>(null);
  adultRegConfirmationOnScreen = signal<string | null>(null);
  adultRegRefundPolicy = signal<string | null>(null);
  adultRegReleaseOfLiability = signal<string | null>(null);
  adultRegCodeOfConduct = signal<string | null>(null);
  refereeRegConfirmationEmail = signal<string | null>(null);
  refereeRegConfirmationOnScreen = signal<string | null>(null);
  recruiterRegConfirmationEmail = signal<string | null>(null);
  recruiterRegConfirmationOnScreen = signal<string | null>(null);
  bAllowRosterViewAdult = signal(false);
  bAllowRosterViewPlayer = signal(false);

  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const c = this.svc.coaches();
      if (!c) return;
      this.regformNameCoach.set(c.regformNameCoach);
      this.adultRegConfirmationEmail.set(c.adultRegConfirmationEmail);
      this.adultRegConfirmationOnScreen.set(c.adultRegConfirmationOnScreen);
      this.adultRegRefundPolicy.set(c.adultRegRefundPolicy);
      this.adultRegReleaseOfLiability.set(c.adultRegReleaseOfLiability);
      this.adultRegCodeOfConduct.set(c.adultRegCodeOfConduct);
      this.refereeRegConfirmationEmail.set(c.refereeRegConfirmationEmail);
      this.refereeRegConfirmationOnScreen.set(c.refereeRegConfirmationOnScreen);
      this.recruiterRegConfirmationEmail.set(c.recruiterRegConfirmationEmail);
      this.recruiterRegConfirmationOnScreen.set(c.recruiterRegConfirmationOnScreen);
      this.bAllowRosterViewAdult.set(c.bAllowRosterViewAdult);
      this.bAllowRosterViewPlayer.set(c.bAllowRosterViewPlayer);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
      this.svc.saveHandler.set(() => this.save());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
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
