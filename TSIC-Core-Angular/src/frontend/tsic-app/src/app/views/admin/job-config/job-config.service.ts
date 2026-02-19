import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
  JobConfigFullDto,
  JobConfigReferenceDataDto,
  UpdateJobConfigGeneralRequest,
  UpdateJobConfigPaymentRequest,
  UpdateJobConfigCommunicationsRequest,
  UpdateJobConfigPlayerRequest,
  UpdateJobConfigTeamsRequest,
  UpdateJobConfigCoachesRequest,
  UpdateJobConfigSchedulingRequest,
  UpdateJobConfigMobileStoreRequest,
  CreateAdminChargeRequest,
  JobAdminChargeDto,
} from '@core/api';

export type TabKey =
  | 'general'
  | 'payment'
  | 'communications'
  | 'player'
  | 'teams'
  | 'coaches'
  | 'scheduling'
  | 'mobileStore';

/** Component-scoped service — provided in shell's providers array. */
@Injectable()
export class JobConfigService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  private readonly baseUrl = '/api/job-config';

  // ── State ─────────────────────────────────────────────

  readonly config = signal<JobConfigFullDto | null>(null);
  readonly referenceData = signal<JobConfigReferenceDataDto | null>(null);
  readonly activeTab = signal<TabKey>('general');
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly dirtyTabs = signal<Set<TabKey>>(new Set());

  readonly isSuperUser = computed(() => this.auth.isSuperuser());

  // ── Computed accessors ────────────────────────────────

  readonly general = computed(() => this.config()?.general ?? null);
  readonly payment = computed(() => this.config()?.payment ?? null);
  readonly communications = computed(() => this.config()?.communications ?? null);
  readonly player = computed(() => this.config()?.player ?? null);
  readonly teams = computed(() => this.config()?.teams ?? null);
  readonly coaches = computed(() => this.config()?.coaches ?? null);
  readonly scheduling = computed(() => this.config()?.scheduling ?? null);
  readonly mobileStore = computed(() => this.config()?.mobileStore ?? null);

  // ── Load ──────────────────────────────────────────────

  loadConfig(): void {
    this.isLoading.set(true);
    this.http.get<JobConfigFullDto>(this.baseUrl).subscribe({
      next: (data) => {
        this.config.set(data);
        this.isLoading.set(false);
        this.dirtyTabs.set(new Set());
      },
      error: (err) => {
        console.error('[JOB-CONFIG] Load failed:', err.status, err.statusText, err.error);
        this.toast.show(`Failed to load job config: ${err.status} ${err.statusText}`, 'danger');
        this.isLoading.set(false);
      },
    });
  }

  loadReferenceData(): void {
    this.http.get<JobConfigReferenceDataDto>(`${this.baseUrl}/reference-data`).subscribe({
      next: (data) => this.referenceData.set(data),
      error: () => this.toast.show('Failed to load reference data.', 'danger'),
    });
  }

  // ── Dirty tracking ────────────────────────────────────

  markDirty(tab: TabKey): void {
    const s = new Set(this.dirtyTabs());
    s.add(tab);
    this.dirtyTabs.set(s);
  }

  markClean(tab: TabKey): void {
    const s = new Set(this.dirtyTabs());
    s.delete(tab);
    this.dirtyTabs.set(s);
  }

  // ── Per-tab save ──────────────────────────────────────

  saveGeneral(req: UpdateJobConfigGeneralRequest): void {
    this.saveTab('general', req);
  }

  savePayment(req: UpdateJobConfigPaymentRequest): void {
    this.saveTab('payment', req);
  }

  saveCommunications(req: UpdateJobConfigCommunicationsRequest): void {
    this.saveTab('communications', req);
  }

  savePlayer(req: UpdateJobConfigPlayerRequest): void {
    this.saveTab('player', req);
  }

  saveTeams(req: UpdateJobConfigTeamsRequest): void {
    this.saveTab('teams', req);
  }

  saveCoaches(req: UpdateJobConfigCoachesRequest): void {
    this.saveTab('coaches', req);
  }

  saveScheduling(req: UpdateJobConfigSchedulingRequest): void {
    this.saveTab('scheduling', req);
  }

  saveMobileStore(req: UpdateJobConfigMobileStoreRequest): void {
    this.saveTab('mobileStore', req);
  }

  // ── Admin Charges (SuperUser only) ────────────────────

  addAdminCharge(req: CreateAdminChargeRequest): void {
    this.isSaving.set(true);
    this.http.post<JobAdminChargeDto>(`${this.baseUrl}/admin-charges`, req).subscribe({
      next: () => {
        this.toast.show('Admin charge added.', 'success');
        this.isSaving.set(false);
        this.loadConfig(); // refresh to pick up new charge
      },
      error: () => {
        this.toast.show('Failed to add admin charge.', 'danger');
        this.isSaving.set(false);
      },
    });
  }

  deleteAdminCharge(chargeId: number): void {
    this.isSaving.set(true);
    this.http.delete(`${this.baseUrl}/admin-charges/${chargeId}`).subscribe({
      next: () => {
        this.toast.show('Admin charge deleted.', 'success');
        this.isSaving.set(false);
        this.loadConfig();
      },
      error: () => {
        this.toast.show('Failed to delete admin charge.', 'danger');
        this.isSaving.set(false);
      },
    });
  }

  // ── Internal ──────────────────────────────────────────

  private saveTab(tab: TabKey, body: unknown): void {
    const slug = tab === 'mobileStore' ? 'mobile-store' : tab;
    this.isSaving.set(true);
    this.http.put(`${this.baseUrl}/${slug}`, body).subscribe({
      next: () => {
        this.toast.show(`${this.tabLabel(tab)} saved.`, 'success');
        this.markClean(tab);
        this.isSaving.set(false);
        this.loadConfig(); // refresh to pick up latest
      },
      error: () => {
        this.toast.show(`Failed to save ${this.tabLabel(tab)}.`, 'danger');
        this.isSaving.set(false);
      },
    });
  }

  private tabLabel(tab: TabKey): string {
    const labels: Record<TabKey, string> = {
      general: 'General',
      payment: 'Payment & Billing',
      communications: 'Communications',
      player: 'Player Registration',
      teams: 'Teams & Club Reps',
      coaches: 'Coaches & Staff',
      scheduling: 'Scheduling',
      mobileStore: 'Mobile & Store',
    };
    return labels[tab];
  }
}
