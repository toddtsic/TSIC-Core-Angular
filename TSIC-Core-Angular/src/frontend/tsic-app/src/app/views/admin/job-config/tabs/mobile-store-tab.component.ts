import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import type { UpdateJobConfigMobileStoreRequest } from '@core/api';

@Component({
  selector: 'app-mobile-store-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './mobile-store-tab.component.html',
})
export class MobileStoreTabComponent {
  protected readonly svc = inject(JobConfigService);

  bEnableTsicteams = signal<boolean | null>(null);
  bEnableMobileRsvp = signal<boolean | null>(null);
  bEnableMobileTeamChat = signal<boolean | null>(null);
  bAllowMobileLogin = signal(false);
  bAllowMobileRegn = signal<boolean | null>(null);
  mobileScoreHoursPastGameEligible = signal(0);

  // SuperUser-only
  mobileJobName = signal<string | null>(null);
  bEnableStore = signal<boolean | null>(null);
  benableStp = signal<boolean | null>(null);
  storeContactEmail = signal<string | null>(null);
  storeRefundPolicy = signal<string | null>(null);
  storePickupDetails = signal<string | null>(null);
  storeSalesTax = signal<number | undefined>(undefined);
  storeTsicrate = signal<number | undefined>(undefined);

  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const m = this.svc.mobileStore();
      if (!m) return;
      this.bEnableTsicteams.set(m.bEnableTsicteams);
      this.bEnableMobileRsvp.set(m.bEnableMobileRsvp);
      this.bEnableMobileTeamChat.set(m.bEnableMobileTeamChat);
      this.bAllowMobileLogin.set(m.bAllowMobileLogin);
      this.bAllowMobileRegn.set(m.bAllowMobileRegn);
      this.mobileScoreHoursPastGameEligible.set(m.mobileScoreHoursPastGameEligible);
      this.mobileJobName.set(m.mobileJobName ?? null);
      this.bEnableStore.set(m.bEnableStore ?? null);
      this.benableStp.set(m.benableStp ?? null);
      this.storeContactEmail.set(m.storeContactEmail ?? null);
      this.storeRefundPolicy.set(m.storeRefundPolicy ?? null);
      this.storePickupDetails.set(m.storePickupDetails ?? null);
      this.storeSalesTax.set(m.storeSalesTax);
      this.storeTsicrate.set(m.storeTsicrate);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
      this.svc.markClean('mobileStore');
    } else {
      this.svc.markDirty('mobileStore');
    }
  }

  save(): void {
    this.svc.saveMobileStore(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigMobileStoreRequest {
    const req: UpdateJobConfigMobileStoreRequest = {
      bEnableTsicteams: this.bEnableTsicteams(),
      bEnableMobileRsvp: this.bEnableMobileRsvp(),
      bEnableMobileTeamChat: this.bEnableMobileTeamChat(),
      bAllowMobileLogin: this.bAllowMobileLogin(),
      bAllowMobileRegn: this.bAllowMobileRegn(),
      mobileScoreHoursPastGameEligible: this.mobileScoreHoursPastGameEligible(),
    };
    if (this.svc.isSuperUser()) {
      req.mobileJobName = this.mobileJobName();
      req.bEnableStore = this.bEnableStore();
      req.benableStp = this.benableStp();
      req.storeContactEmail = this.storeContactEmail();
      req.storeRefundPolicy = this.storeRefundPolicy();
      req.storePickupDetails = this.storePickupDetails();
      req.storeSalesTax = this.storeSalesTax();
      req.storeTsicrate = this.storeTsicrate();
    }
    return req;
  }
}
