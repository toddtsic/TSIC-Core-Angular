import { Component, inject, ChangeDetectionStrategy, OnInit, HostListener, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobConfigService, type TabKey } from './job-config.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { HasUnsavedChanges } from '../../../infrastructure/guards/unsaved-changes.guard';
import { GeneralTabComponent } from './tabs/general-tab.component';
import { PaymentTabComponent } from './tabs/payment-tab.component';
import { CommunicationsTabComponent } from './tabs/communications-tab.component';
import { PlayerTabComponent } from './tabs/player-tab.component';
import { TeamsTabComponent } from './tabs/teams-tab.component';
import { CoachesTabComponent } from './tabs/coaches-tab.component';
import { SchedulingTabComponent } from './tabs/scheduling-tab.component';
import { MobileStoreTabComponent } from './tabs/mobile-store-tab.component';
import { BrandingTabComponent } from './tabs/branding-tab.component';
import { DdlOptionsComponent } from '../ddl-options/ddl-options.component';

interface TabDef {
  key: TabKey;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-job-config',
  standalone: true,
  imports: [
    CommonModule,
    ConfirmDialogComponent,
    GeneralTabComponent,
    PaymentTabComponent,
    CommunicationsTabComponent,
    PlayerTabComponent,
    TeamsTabComponent,
    CoachesTabComponent,
    SchedulingTabComponent,
    MobileStoreTabComponent,
    BrandingTabComponent,
    DdlOptionsComponent,
  ],
  providers: [JobConfigService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './job-config.component.html',
  styleUrl: './job-config.component.scss',
})
export class JobConfigComponent implements OnInit, HasUnsavedChanges {
  protected readonly svc = inject(JobConfigService);

  readonly tabs: TabDef[] = [
    { key: 'general', label: 'General', icon: 'bi-gear' },
    { key: 'branding', label: 'Branding', icon: 'bi-palette' },
    { key: 'payment', label: 'Payment & Billing', icon: 'bi-credit-card' },
    { key: 'communications', label: 'Communications', icon: 'bi-envelope' },
    { key: 'player', label: 'Player Registration', icon: 'bi-person' },
    { key: 'teams', label: 'Teams & Club Reps', icon: 'bi-shield' },
    { key: 'coaches', label: 'Coaches & Staff', icon: 'bi-people' },
    { key: 'scheduling', label: 'Scheduling', icon: 'bi-calendar-event' },
    { key: 'mobileStore', label: 'Mobile & Store', icon: 'bi-phone' },
    { key: 'ddlOptions', label: 'Dropdown Options', icon: 'bi-list-ul' },
  ];

  // ── Unsaved-changes dialog state ────────────────────
  showDiscardDialog = signal(false);
  pendingTabKey = signal<TabKey | null>(null);

  ngOnInit(): void {
    this.svc.loadConfig();
    this.svc.loadReferenceData();
  }

  // ── HasUnsavedChanges interface ─────────────────────
  hasUnsavedChanges(): boolean {
    return this.svc.dirtyTabs().size > 0;
  }

  // ── Browser close/refresh guard ─────────────────────
  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  // ── Tab switching with dirty guard ──────────────────
  selectTab(key: TabKey): void {
    const currentTab = this.svc.activeTab();
    if (key === currentTab) return;

    if (this.svc.dirtyTabs().has(currentTab)) {
      this.pendingTabKey.set(key);
      this.showDiscardDialog.set(true);
      return;
    }

    this.svc.activeTab.set(key);
  }

  onDiscardConfirmed(): void {
    const pending = this.pendingTabKey();
    if (pending) {
      this.svc.markClean(this.svc.activeTab());
      this.svc.activeTab.set(pending);
    }
    this.showDiscardDialog.set(false);
    this.pendingTabKey.set(null);
  }

  onDiscardCancelled(): void {
    this.showDiscardDialog.set(false);
    this.pendingTabKey.set(null);
  }

  isDirty(key: TabKey): boolean {
    return this.svc.dirtyTabs().has(key);
  }

  onDdlDirtyChange(dirty: boolean): void {
    if (dirty) {
      this.svc.markDirty('ddlOptions');
    } else {
      this.svc.markClean('ddlOptions');
    }
  }

  onTabKeydown(event: KeyboardEvent): void {
    const keys = ['ArrowLeft', 'ArrowRight', 'Home', 'End'];
    if (!keys.includes(event.key)) return;

    event.preventDefault();
    const currentIndex = this.tabs.findIndex(t => t.key === this.svc.activeTab());
    let nextIndex = currentIndex;

    switch (event.key) {
      case 'ArrowRight':
        nextIndex = (currentIndex + 1) % this.tabs.length;
        break;
      case 'ArrowLeft':
        nextIndex = (currentIndex - 1 + this.tabs.length) % this.tabs.length;
        break;
      case 'Home':
        nextIndex = 0;
        break;
      case 'End':
        nextIndex = this.tabs.length - 1;
        break;
    }

    this.selectTab(this.tabs[nextIndex].key);
    // Focus the target tab button after the dialog check resolves
    if (!this.showDiscardDialog()) {
      const buttons = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('.tab-btn');
      buttons[nextIndex]?.focus();
    }
  }
}
