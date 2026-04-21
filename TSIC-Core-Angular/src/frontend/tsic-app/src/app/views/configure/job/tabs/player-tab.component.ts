import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_TOOLS, JOB_CONFIG_RTE_HEIGHT, toDateOnly } from '../shared/rte-config';
import type { UpdateJobConfigPlayerRequest } from '@core/api';

@Component({
  selector: 'app-player-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './player-tab.component.html',
})
export class PlayerTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = JOB_CONFIG_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  bRegistrationAllowPlayer = linkedSignal(() => this.svc.player()?.bRegistrationAllowPlayer ?? null);
  bPlayerRegRequiresToken = linkedSignal(() => this.svc.player()?.bPlayerRegRequiresToken ?? null);
  regformNamePlayer = linkedSignal(() => this.svc.player()?.regformNamePlayer ?? '');
  coreRegformPlayer = linkedSignal(() => this.svc.player()?.coreRegformPlayer ?? null);
  playerRegConfirmationEmail = linkedSignal(() => this.svc.player()?.playerRegConfirmationEmail ?? null);
  playerRegConfirmationOnScreen = linkedSignal(() => this.svc.player()?.playerRegConfirmationOnScreen ?? null);
  playerRegRefundPolicy = linkedSignal(() => this.svc.player()?.playerRegRefundPolicy ?? null);
  playerRegReleaseOfLiability = linkedSignal(() => this.svc.player()?.playerRegReleaseOfLiability ?? null);
  playerRegCodeOfConduct = linkedSignal(() => this.svc.player()?.playerRegCodeOfConduct ?? null);
  playerRegCovid19Waiver = linkedSignal(() => this.svc.player()?.playerRegCovid19Waiver ?? null);
  playerRegMultiPlayerDiscountMin = linkedSignal(() => this.svc.player()?.playerRegMultiPlayerDiscountMin ?? null);
  playerRegMultiPlayerDiscountPercent = linkedSignal(() => this.svc.player()?.playerRegMultiPlayerDiscountPercent ?? null);
  uslaxNumberValidThroughDate = linkedSignal(() => toDateOnly(this.svc.player()?.uslaxNumberValidThroughDate) ?? null);

  // SuperUser-only
  bOfferPlayerRegsaverInsurance = linkedSignal(() => this.svc.player()?.bOfferPlayerRegsaverInsurance ?? null);
  momLabel = linkedSignal(() => this.svc.player()?.momLabel ?? null);
  dadLabel = linkedSignal(() => this.svc.player()?.dadLabel ?? null);

  private readonly cleanSnapshot = computed(() => {
    const p = this.svc.player();
    if (!p) return '';
    const req: UpdateJobConfigPlayerRequest = {
      bRegistrationAllowPlayer: p.bRegistrationAllowPlayer,
      bPlayerRegRequiresToken: p.bPlayerRegRequiresToken,
      regformNamePlayer: p.regformNamePlayer,
      coreRegformPlayer: p.coreRegformPlayer,
      playerRegConfirmationEmail: p.playerRegConfirmationEmail,
      playerRegConfirmationOnScreen: p.playerRegConfirmationOnScreen,
      playerRegRefundPolicy: p.playerRegRefundPolicy,
      playerRegReleaseOfLiability: p.playerRegReleaseOfLiability,
      playerRegCodeOfConduct: p.playerRegCodeOfConduct,
      playerRegCovid19Waiver: p.playerRegCovid19Waiver,
      playerRegMultiPlayerDiscountMin: p.playerRegMultiPlayerDiscountMin,
      playerRegMultiPlayerDiscountPercent: p.playerRegMultiPlayerDiscountPercent,
      uslaxNumberValidThroughDate: toDateOnly(p.uslaxNumberValidThroughDate) ?? null,
    };
    if (this.svc.isSuperUser()) {
      req.bOfferPlayerRegsaverInsurance = p.bOfferPlayerRegsaverInsurance ?? null;
      req.momLabel = p.momLabel ?? null;
      req.dadLabel = p.dadLabel ?? null;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
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
      bPlayerRegRequiresToken: this.bPlayerRegRequiresToken(),
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
      uslaxNumberValidThroughDate: this.uslaxNumberValidThroughDate(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferPlayerRegsaverInsurance = this.bOfferPlayerRegsaverInsurance();
      req.momLabel = this.momLabel();
      req.dadLabel = this.dadLabel();
    }
    return req;
  }
}
