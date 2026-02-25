import { Component, inject, ChangeDetectionStrategy, OnInit, computed, linkedSignal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import { toDateOnly } from '../shared/rte-config';
import type { UpdateJobConfigGeneralRequest } from '@core/api';

@Component({
  selector: 'app-general-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './general-tab.component.html',
})
export class GeneralTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  // ── Local form model ──────────────────────────────────

  jobName = linkedSignal(() => this.svc.general()?.jobName ?? null);
  jobDescription = linkedSignal(() => this.svc.general()?.jobDescription ?? null);
  jobTagline = linkedSignal(() => this.svc.general()?.jobTagline ?? null);
  season = linkedSignal(() => this.svc.general()?.season ?? null);
  year = linkedSignal(() => this.svc.general()?.year ?? null);
  expiryUsers = linkedSignal(() => toDateOnly(this.svc.general()?.expiryUsers) ?? '');
  displayName = linkedSignal(() => this.svc.general()?.displayName ?? null);
  searchenginKeywords = linkedSignal(() => this.svc.general()?.searchenginKeywords ?? null);
  searchengineDescription = linkedSignal(() => this.svc.general()?.searchengineDescription ?? null);

  // SuperUser-only fields
  jobNameQbp = linkedSignal(() => this.svc.general()?.jobNameQbp ?? null);
  expiryAdmin = linkedSignal(() => toDateOnly(this.svc.general()?.expiryAdmin) ?? null);
  jobTypeId = linkedSignal(() => this.svc.general()?.jobTypeId);
  sportId = linkedSignal(() => this.svc.general()?.sportId ?? null);
  customerId = linkedSignal(() => this.svc.general()?.customerId ?? null);
  billingTypeId = linkedSignal(() => this.svc.general()?.billingTypeId);
  bSuspendPublic = linkedSignal(() => this.svc.general()?.bSuspendPublic ?? null);
  jobCode = linkedSignal(() => this.svc.general()?.jobCode ?? null);

  private readonly cleanSnapshot = computed(() => {
    const g = this.svc.general();
    if (!g) return '';
    const req: UpdateJobConfigGeneralRequest = {
      jobName: g.jobName,
      jobDescription: g.jobDescription,
      jobTagline: g.jobTagline,
      season: g.season,
      year: g.year,
      expiryUsers: toDateOnly(g.expiryUsers) ?? '',
      displayName: g.displayName,
      searchenginKeywords: g.searchenginKeywords,
      searchengineDescription: g.searchengineDescription,
    };
    if (this.svc.isSuperUser()) {
      req.jobNameQbp = g.jobNameQbp ?? null;
      req.expiryAdmin = toDateOnly(g.expiryAdmin) ?? null;
      req.jobTypeId = g.jobTypeId;
      req.sportId = g.sportId ?? null;
      req.customerId = g.customerId ?? null;
      req.billingTypeId = g.billingTypeId;
      req.bSuspendPublic = g.bSuspendPublic ?? null;
      req.jobCode = g.jobCode ?? null;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
      this.svc.markClean('general');
    } else {
      this.svc.markDirty('general');
    }
  }

  save(): void {
    this.svc.saveGeneral(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigGeneralRequest {
    const req: UpdateJobConfigGeneralRequest = {
      jobName: this.jobName(),
      jobDescription: this.jobDescription(),
      jobTagline: this.jobTagline(),
      season: this.season(),
      year: this.year(),
      expiryUsers: this.expiryUsers(),
      displayName: this.displayName(),
      searchenginKeywords: this.searchenginKeywords(),
      searchengineDescription: this.searchengineDescription(),
    };
    if (this.svc.isSuperUser()) {
      req.jobNameQbp = this.jobNameQbp();
      req.expiryAdmin = this.expiryAdmin();
      req.jobTypeId = this.jobTypeId();
      req.sportId = this.sportId();
      req.customerId = this.customerId();
      req.billingTypeId = this.billingTypeId();
      req.bSuspendPublic = this.bSuspendPublic();
      req.jobCode = this.jobCode();
    }
    return req;
  }
}
