import { Component, ChangeDetectionStrategy, ViewChild, computed, signal, output, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobService } from '@infrastructure/services/job.service';
import { ScheduleCascadeService } from '../schedule-config/schedule-cascade.service';
import type { DevResetOptions } from '../schedule-config/schedule-config.types';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { BuildRulesTabComponent } from './tabs/build-rules-tab.component';
import { BuildOrderTabComponent } from './tabs/build-order-tab.component';
import { WavesTabComponent } from './tabs/waves-tab.component';
import { DatesTabComponent } from './tabs/dates-tab.component';
import { FieldsTabComponent } from './tabs/fields-tab.component';
import { RoundsTabComponent } from './tabs/rounds-tab.component';
import { GridTabComponent } from './tabs/grid-tab.component';

export type ScheduleConfigTab = 'dates' | 'fields' | 'buildRules' | 'rounds' | 'waves' | 'buildOrder' | 'grid';

interface TabDef {
  key: ScheduleConfigTab;
  label: string;
  icon: string;
}

/**
 * Schedule Config Panel — tabbed configuration surface for scheduling.
 * Replaces the old event-summary-panel accordion stepper.
 *
 * Tabs: Dates, Fields, Build Rules, Rounds Per Day, Waves, Build Order, Grid.
 * Self-sufficient: injects services directly, minimal inputs from parent.
 */
@Component({
  selector: 'app-schedule-config-panel',
  standalone: true,
  imports: [
    CommonModule,
    TsicDialogComponent,
    BuildRulesTabComponent,
    BuildOrderTabComponent,
    WavesTabComponent,
    DatesTabComponent,
    FieldsTabComponent,
    RoundsTabComponent,
    GridTabComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './schedule-config-panel.component.html',
  styleUrl: './schedule-config-panel.component.scss',
})
export class ScheduleConfigPanelComponent {
  private readonly jobSvc = inject(JobService);
  private readonly cascadeSvc = inject(ScheduleCascadeService);

  /** Whether games already exist at event level (controls Build vs Re-Build label). */
  readonly hasGames = input(false);

  /** Expose cascade signal for template guard (defer tabs until loaded). */
  readonly cascade = this.cascadeSvc.cascade;

  // ── Tab ViewChild refs (only active tab is resolved at a time) ──
  @ViewChild(DatesTabComponent) private datesTab?: DatesTabComponent;
  @ViewChild(FieldsTabComponent) private fieldsTab?: FieldsTabComponent;
  @ViewChild(BuildRulesTabComponent) private buildRulesTab?: BuildRulesTabComponent;
  @ViewChild(RoundsTabComponent) private roundsTab?: RoundsTabComponent;
  @ViewChild(BuildOrderTabComponent) private buildOrderTab?: BuildOrderTabComponent;
  @ViewChild(GridTabComponent) private gridTab?: GridTabComponent;

  // ── Derived state ──
  readonly eventName = computed(() => this.jobSvc.currentJob()?.jobName ?? '');

  // ── Outputs ──
  buildRequested = output<void>();
  resetConfirmed = output<DevResetOptions>();

  // ── Tab state ──
  readonly tabs: TabDef[] = [
    { key: 'dates', label: 'Dates', icon: 'bi-calendar-event' },
    { key: 'fields', label: 'Fields', icon: 'bi-grid-3x3' },
    { key: 'buildRules', label: 'Build Rules', icon: 'bi-sliders' },
    { key: 'rounds', label: 'Rounds / Day', icon: 'bi-arrow-repeat' },
    { key: 'waves', label: 'Waves', icon: 'bi-water' },
    { key: 'buildOrder', label: 'Build Order', icon: 'bi-sort-numeric-down' },
    { key: 'grid', label: 'Config Summary', icon: 'bi-table' },
  ];

  activeTab = signal<ScheduleConfigTab>('dates');

  // ── Reset dialog state ──
  readonly showResetDialog = signal(false);

  selectTab(key: ScheduleConfigTab): void {
    this.activeTab.set(key);
  }

  // ── Reset dialog ──

  openResetDialog(): void {
    this.showResetDialog.set(true);
  }

  onResetCancelled(): void {
    this.showResetDialog.set(false);
  }

  onResetConfirmed(): void {
    this.showResetDialog.set(false);
    this.resetConfirmed.emit({
      games: true,
      strategyProfiles: false,
      pairings: false,
      dates: true,
      fieldTimeslots: true,
    });
  }

  // ── Explicit reload (called by parent after reset / cascade reload) ──

  reloadActiveTab(): void {
    switch (this.activeTab()) {
      case 'dates': this.datesTab?.reload(); break;
      case 'fields': this.fieldsTab?.reload(); break;
      case 'buildRules': this.buildRulesTab?.reload(); break;
      case 'rounds': this.roundsTab?.reload(); break;
      case 'buildOrder': this.buildOrderTab?.reload(); break;
      case 'grid': this.gridTab?.reload(); break;
      // waves tab uses computed signals — no reload needed
    }
  }

  // ── Tab keyboard nav ──

  onTabKeydown(event: KeyboardEvent): void {
    const tabKeys = this.tabs.map(t => t.key);
    const current = tabKeys.indexOf(this.activeTab());
    if (current < 0) return;

    let next: number | null = null;
    if (event.key === 'ArrowRight') next = (current + 1) % tabKeys.length;
    if (event.key === 'ArrowLeft') next = (current - 1 + tabKeys.length) % tabKeys.length;

    if (next !== null) {
      event.preventDefault();
      this.activeTab.set(tabKeys[next]);
      const btn = (event.target as HTMLElement)
        ?.parentElement?.querySelectorAll('.tab-btn')[next] as HTMLElement | null;
      btn?.focus();
    }
  }
}
