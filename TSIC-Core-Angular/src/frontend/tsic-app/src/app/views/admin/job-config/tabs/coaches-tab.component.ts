import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import type { UpdateJobConfigCoachesRequest } from '@core/api';

@Component({
  selector: 'app-coaches-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './coaches-tab.component.html',
})
export class CoachesTabComponent {
  protected readonly svc = inject(JobConfigService);

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
    }, { allowSignalWrites: true });
  }

  onFieldChange(): void { this.svc.markDirty('coaches'); }

  save(): void {
    const req: UpdateJobConfigCoachesRequest = {
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
    this.svc.saveCoaches(req);
  }
}
