import { Component, inject, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobConfigService, type TabKey } from './job-config.service';
import { GeneralTabComponent } from './tabs/general-tab.component';
import { PaymentTabComponent } from './tabs/payment-tab.component';
import { CommunicationsTabComponent } from './tabs/communications-tab.component';
import { PlayerTabComponent } from './tabs/player-tab.component';
import { TeamsTabComponent } from './tabs/teams-tab.component';
import { CoachesTabComponent } from './tabs/coaches-tab.component';
import { SchedulingTabComponent } from './tabs/scheduling-tab.component';
import { MobileStoreTabComponent } from './tabs/mobile-store-tab.component';

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
    GeneralTabComponent,
    PaymentTabComponent,
    CommunicationsTabComponent,
    PlayerTabComponent,
    TeamsTabComponent,
    CoachesTabComponent,
    SchedulingTabComponent,
    MobileStoreTabComponent,
  ],
  providers: [JobConfigService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './job-config.component.html',
  styleUrl: './job-config.component.scss',
})
export class JobConfigComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  readonly tabs: TabDef[] = [
    { key: 'general', label: 'General', icon: 'bi-gear' },
    { key: 'payment', label: 'Payment & Billing', icon: 'bi-credit-card' },
    { key: 'communications', label: 'Communications', icon: 'bi-envelope' },
    { key: 'player', label: 'Player Registration', icon: 'bi-person' },
    { key: 'teams', label: 'Teams & Club Reps', icon: 'bi-shield' },
    { key: 'coaches', label: 'Coaches & Staff', icon: 'bi-people' },
    { key: 'scheduling', label: 'Scheduling', icon: 'bi-calendar-event' },
    { key: 'mobileStore', label: 'Mobile & Store', icon: 'bi-phone' },
  ];

  ngOnInit(): void {
    this.svc.loadConfig();
    this.svc.loadReferenceData();
  }

  selectTab(key: TabKey): void {
    this.svc.activeTab.set(key);
  }

  isDirty(key: TabKey): boolean {
    return this.svc.dirtyTabs().has(key);
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
    const buttons = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('.tab-btn');
    buttons[nextIndex]?.focus();
  }
}
