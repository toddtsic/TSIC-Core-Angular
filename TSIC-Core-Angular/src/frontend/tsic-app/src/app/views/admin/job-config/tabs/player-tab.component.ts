import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import type { UpdateJobConfigPlayerRequest } from '@core/api';

@Component({
  selector: 'app-player-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './player-tab.component.html',
})
export class PlayerTabComponent {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  bRegistrationAllowPlayer = signal<boolean | null>(null);
  regformNamePlayer = signal('');
  coreRegformPlayer = signal<string | null>(null);
  playerRegConfirmationEmail = signal<string | null>(null);
  playerRegConfirmationOnScreen = signal<string | null>(null);
  playerRegRefundPolicy = signal<string | null>(null);
  playerRegReleaseOfLiability = signal<string | null>(null);
  playerRegCodeOfConduct = signal<string | null>(null);
  playerRegCovid19Waiver = signal<string | null>(null);
  playerRegMultiPlayerDiscountMin = signal(0);
  playerRegMultiPlayerDiscountPercent = signal(0);

  // SuperUser-only
  bOfferPlayerRegsaverInsurance = signal<boolean | null>(null);
  momLabel = signal<string | null>(null);
  dadLabel = signal<string | null>(null);
  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const p = this.svc.player();
      if (!p) return;
      this.bRegistrationAllowPlayer.set(p.bRegistrationAllowPlayer);
      this.regformNamePlayer.set(p.regformNamePlayer);
      this.coreRegformPlayer.set(p.coreRegformPlayer);
      this.playerRegConfirmationEmail.set(p.playerRegConfirmationEmail);
      this.playerRegConfirmationOnScreen.set(p.playerRegConfirmationOnScreen);
      this.playerRegRefundPolicy.set(p.playerRegRefundPolicy);
      this.playerRegReleaseOfLiability.set(p.playerRegReleaseOfLiability);
      this.playerRegCodeOfConduct.set(p.playerRegCodeOfConduct);
      this.playerRegCovid19Waiver.set(p.playerRegCovid19Waiver);
      this.playerRegMultiPlayerDiscountMin.set(p.playerRegMultiPlayerDiscountMin);
      this.playerRegMultiPlayerDiscountPercent.set(p.playerRegMultiPlayerDiscountPercent);
      this.bOfferPlayerRegsaverInsurance.set(p.bOfferPlayerRegsaverInsurance ?? null);
      this.momLabel.set(p.momLabel ?? null);
      this.dadLabel.set(p.dadLabel ?? null);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
      this.svc.markClean('player');
    } else {
      this.svc.markDirty('player');
    }
  }

  onRteChange(field: string, event: any): void {
    const sig = (this as any)[field];
    if (sig?.set) sig.set(event.value ?? '');
    this.onFieldChange();
  }

  save(): void {
    this.svc.savePlayer(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigPlayerRequest {
    const req: UpdateJobConfigPlayerRequest = {
      bRegistrationAllowPlayer: this.bRegistrationAllowPlayer(),
      regformNamePlayer: this.regformNamePlayer(),
      coreRegformPlayer: this.coreRegformPlayer(),
      playerRegConfirmationEmail: this.playerRegConfirmationEmail(),
      playerRegConfirmationOnScreen: this.playerRegConfirmationOnScreen(),
      playerRegRefundPolicy: this.playerRegRefundPolicy(),
      playerRegReleaseOfLiability: this.playerRegReleaseOfLiability(),
      playerRegCodeOfConduct: this.playerRegCodeOfConduct(),
      playerRegCovid19Waiver: this.playerRegCovid19Waiver(),
      playerRegMultiPlayerDiscountMin: this.playerRegMultiPlayerDiscountMin(),
      playerRegMultiPlayerDiscountPercent: this.playerRegMultiPlayerDiscountPercent(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferPlayerRegsaverInsurance = this.bOfferPlayerRegsaverInsurance();
      req.momLabel = this.momLabel();
      req.dadLabel = this.dadLabel();
    }
    return req;
  }
}
