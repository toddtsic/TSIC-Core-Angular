import { Component, inject, ChangeDetectionStrategy, OnInit, computed, linkedSignal, signal } from '@angular/core';
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
  styles: [`
    .id-copy {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      max-width: 100%;
      padding: var(--space-1) var(--space-2);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
      background: var(--bs-tertiary-bg);
      color: var(--brand-text-muted);
      font-family: var(--font-family-mono, monospace);
      font-size: var(--font-size-2xs);
      line-height: 1.2;
      cursor: pointer;
      transition: color 0.15s, border-color 0.15s;
    }
    .id-copy:hover { color: var(--brand-text); border-color: var(--bs-primary); }
    .id-copy:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
    .id-copy__value {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .id-copy .bi-check-lg { color: var(--bs-success); }
    @media (prefers-reduced-motion: reduce) {
      .id-copy { transition: none; }
    }
  `],
})
export class GeneralTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  /** Transient "Copied!" state for the Job ID copy chip (SuperUser-only). */
  readonly jobIdCopied = signal(false);

  // ── Local form model ──────────────────────────────────

  jobName = linkedSignal(() => this.svc.general()?.jobName ?? null);
  season = linkedSignal(() => this.svc.general()?.season ?? null);
  year = linkedSignal(() => this.svc.general()?.year ?? null);
  expiryUsers = linkedSignal(() => toDateOnly(this.svc.general()?.expiryUsers) ?? '');
  // SuperUser-only fields
  jobPath = linkedSignal(() => this.svc.general()?.jobPath ?? null);
  jobDescription = linkedSignal(() => this.svc.general()?.jobDescription ?? null);
  jobNameQbp = linkedSignal(() => this.svc.general()?.jobNameQbp ?? null);
  expiryAdmin = linkedSignal(() => toDateOnly(this.svc.general()?.expiryAdmin) ?? null);
  jobTypeId = linkedSignal(() => this.svc.general()?.jobTypeId);
  sportId = linkedSignal(() => this.svc.general()?.sportId ?? null);
  customerId = linkedSignal(() => this.svc.general()?.customerId ?? null);
  billingTypeId = linkedSignal(() => this.svc.general()?.billingTypeId);
  jobCode = linkedSignal(() => this.svc.general()?.jobCode ?? null);

  private readonly cleanSnapshot = computed(() => {
    const g = this.svc.general();
    if (!g) return '';
    const req: UpdateJobConfigGeneralRequest = {
      jobName: g.jobName,
      jobDescription: null,
      season: g.season,
      year: g.year,
      expiryUsers: toDateOnly(g.expiryUsers) ?? '',
      jobTagline: g.jobTagline,
      searchenginKeywords: g.searchenginKeywords,
      searchengineDescription: g.searchengineDescription,
    };
    if (this.svc.isSuperUser()) {
      req.jobPath = g.jobPath ?? null;
      req.jobDescription = g.jobDescription ?? null;
      req.jobNameQbp = g.jobNameQbp ?? null;
      req.expiryAdmin = toDateOnly(g.expiryAdmin) ?? null;
      req.jobTypeId = g.jobTypeId;
      req.sportId = g.sportId ?? null;
      req.customerId = g.customerId ?? null;
      req.billingTypeId = g.billingTypeId;
      req.jobCode = g.jobCode ?? null;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  /** Copy the Job ID to the clipboard for pasting into SQL queries. */
  copyJobId(jobId: string): void {
    navigator.clipboard.writeText(jobId).then(() => {
      this.jobIdCopied.set(true);
      setTimeout(() => this.jobIdCopied.set(false), 2000);
    });
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
      jobDescription: null,
      season: this.season(),
      year: this.year(),
      expiryUsers: this.expiryUsers(),
      jobTagline: this.svc.general()?.jobTagline ?? null,
      searchenginKeywords: this.svc.general()?.searchenginKeywords ?? null,
      searchengineDescription: this.svc.general()?.searchengineDescription ?? null,
    };
    if (this.svc.isSuperUser()) {
      req.jobPath = this.jobPath();
      req.jobDescription = this.jobDescription();
      req.jobNameQbp = this.jobNameQbp();
      req.expiryAdmin = this.expiryAdmin();
      req.jobTypeId = this.jobTypeId();
      req.sportId = this.sportId();
      req.customerId = this.customerId();
      req.billingTypeId = this.billingTypeId();
      req.jobCode = this.jobCode();
    }
    return req;
  }
}
