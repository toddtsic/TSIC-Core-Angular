import { Component, inject, ChangeDetectionStrategy, OnInit, HostListener, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
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
  superUserOnly?: boolean;
}

@Component({
  selector: 'app-job-config',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
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

  private readonly allTabs: TabDef[] = [
    { key: 'general', label: 'General', icon: '' },
    { key: 'branding', label: 'Branding', icon: '', superUserOnly: true },
    { key: 'payment', label: 'Payment', icon: '' },
    { key: 'communications', label: 'Comms', icon: '' },
    { key: 'player', label: 'Players', icon: '' },
    { key: 'teams', label: 'Teams', icon: '' },
    { key: 'coaches', label: 'Adult', icon: '' },
    { key: 'scheduling', label: 'Scheduling', icon: '' },
    { key: 'mobileStore', label: 'Mobile/Store', icon: '' },
    { key: 'ddlOptions', label: 'Dropdowns', icon: '', superUserOnly: true },
  ];

  // SuperUser-only tabs (e.g. Branding) are hidden entirely for other roles.
  readonly tabs = computed(() =>
    this.allTabs.filter(t => !t.superUserOnly || this.svc.isSuperUser()));

  // Tabs that carry registration/visibility switches now owned by the Quick Links
  // editor. On these, those switches render read-only with a pointer to Quick Links.
  private readonly quickLinksManagedTabs = new Set<TabKey>([
    'player', 'teams', 'coaches', 'scheduling', 'mobileStore',
  ]);
  readonly showQuickLinksNote = computed(() => this.quickLinksManagedTabs.has(this.svc.activeTab()));

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

  // ── FAB save ──────────────────────────────────────────
  isActiveTabDirty = computed(() => this.svc.dirtyTabs().has(this.svc.activeTab()));

  onFabSave(): void {
    const handler = this.svc.saveHandler();
    if (handler) handler();
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
    const tabs = this.tabs();
    const currentIndex = tabs.findIndex(t => t.key === this.svc.activeTab());
    let nextIndex = currentIndex;

    switch (event.key) {
      case 'ArrowRight':
        nextIndex = (currentIndex + 1) % tabs.length;
        break;
      case 'ArrowLeft':
        nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
        break;
      case 'Home':
        nextIndex = 0;
        break;
      case 'End':
        nextIndex = tabs.length - 1;
        break;
    }

    this.selectTab(tabs[nextIndex].key);
    // Focus the target tab button after the dialog check resolves
    if (!this.showDiscardDialog()) {
      const buttons = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('.tab-btn');
      buttons[nextIndex]?.focus();
    }
  }
}
