import { Component, inject, ChangeDetectionStrategy, OnInit, signal, effect } from '@angular/core';
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

  jobName = signal<string | null>(null);
  jobDescription = signal<string | null>(null);
  jobTagline = signal<string | null>(null);
  season = signal<string | null>(null);
  year = signal<string | null>(null);
  expiryUsers = signal('');
  displayName = signal<string | null>(null);
  searchenginKeywords = signal<string | null>(null);
  searchengineDescription = signal<string | null>(null);

  // SuperUser-only fields
  jobNameQbp = signal<string | null>(null);
  expiryAdmin = signal<string | null>(null);
  jobTypeId = signal<number | undefined>(undefined);
  sportId = signal<string | null>(null);
  customerId = signal<string | null>(null);
  billingTypeId = signal<number | undefined>(undefined);
  bSuspendPublic = signal<boolean | null>(null);
  jobCode = signal<string | null>(null);

  private cleanSnapshot = '';

  constructor() {
    // Sync from service data to local form signals
    effect(() => {
      const g = this.svc.general();
      if (!g) return;
      this.jobName.set(g.jobName);
      this.jobDescription.set(g.jobDescription);
      this.jobTagline.set(g.jobTagline);
      this.season.set(g.season);
      this.year.set(g.year);
      this.expiryUsers.set(toDateOnly(g.expiryUsers) ?? '');
      this.displayName.set(g.displayName);
      this.searchenginKeywords.set(g.searchenginKeywords);
      this.searchengineDescription.set(g.searchengineDescription);
      // SuperUser-only (may be undefined for non-super)
      this.jobNameQbp.set(g.jobNameQbp ?? null);
      this.expiryAdmin.set(toDateOnly(g.expiryAdmin));
      this.jobTypeId.set(g.jobTypeId);
      this.sportId.set(g.sportId ?? null);
      this.customerId.set(g.customerId ?? null);
      this.billingTypeId.set(g.billingTypeId);
      this.bSuspendPublic.set(g.bSuspendPublic ?? null);
      this.jobCode.set(g.jobCode ?? null);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
    });
  }

  ngOnInit(): void {}

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
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

    // Include super-only fields (service ignores them for non-super)
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
