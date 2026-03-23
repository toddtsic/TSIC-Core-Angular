import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
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
export class MobileStoreTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  bEnableTsicteams = linkedSignal(() => this.svc.mobileStore()?.bEnableTsicteams ?? null);
  bEnableMobileRsvp = linkedSignal(() => this.svc.mobileStore()?.bEnableMobileRsvp ?? null);
  bEnableMobileTeamChat = linkedSignal(() => this.svc.mobileStore()?.bEnableMobileTeamChat ?? null);
  bAllowMobileLogin = linkedSignal(() => this.svc.mobileStore()?.bAllowMobileLogin ?? false);
  bAllowMobileRegn = linkedSignal(() => this.svc.mobileStore()?.bAllowMobileRegn ?? null);
  mobileScoreHoursPastGameEligible = linkedSignal(() => this.svc.mobileStore()?.mobileScoreHoursPastGameEligible ?? null);

  // SuperUser-only
  mobileJobName = linkedSignal(() => this.svc.mobileStore()?.mobileJobName ?? null);
  bEnableStore = linkedSignal(() => this.svc.mobileStore()?.bEnableStore ?? null);
  benableStp = linkedSignal(() => this.svc.mobileStore()?.benableStp ?? null);
  storeContactEmail = linkedSignal(() => this.svc.mobileStore()?.storeContactEmail ?? null);
  storeRefundPolicy = linkedSignal(() => this.svc.mobileStore()?.storeRefundPolicy ?? null);
  storePickupDetails = linkedSignal(() => this.svc.mobileStore()?.storePickupDetails ?? null);
  storeSalesTax = linkedSignal(() => this.svc.mobileStore()?.storeSalesTax);
  storeTsicrate = linkedSignal(() => this.svc.mobileStore()?.storeTsicrate);

  private readonly cleanSnapshot = computed(() => {
    const m = this.svc.mobileStore();
    if (!m) return '';
    const req: UpdateJobConfigMobileStoreRequest = {
      bEnableTsicteams: m.bEnableTsicteams,
      bEnableMobileRsvp: m.bEnableMobileRsvp,
      bEnableMobileTeamChat: m.bEnableMobileTeamChat,
      bAllowMobileLogin: m.bAllowMobileLogin,
      bAllowMobileRegn: m.bAllowMobileRegn,
      mobileScoreHoursPastGameEligible: m.mobileScoreHoursPastGameEligible,
    };
    if (this.svc.isSuperUser()) {
      req.mobileJobName = m.mobileJobName ?? null;
      req.bEnableStore = m.bEnableStore ?? null;
      req.benableStp = m.benableStp ?? null;
      req.storeContactEmail = m.storeContactEmail ?? null;
      req.storeRefundPolicy = m.storeRefundPolicy ?? null;
      req.storePickupDetails = m.storePickupDetails ?? null;
      req.storeSalesTax = m.storeSalesTax;
      req.storeTsicrate = m.storeTsicrate;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
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
