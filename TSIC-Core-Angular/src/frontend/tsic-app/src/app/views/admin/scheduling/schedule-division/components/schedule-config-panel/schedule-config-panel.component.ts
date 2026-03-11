import { Component, ChangeDetectionStrategy, computed, signal, output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
    FormsModule,
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
    { key: 'grid', label: 'AG Grid', icon: 'bi-table' },
  ];

  activeTab = signal<ScheduleConfigTab>('buildRules');

  // ── Reset dialog state ──
  readonly showResetDialog = signal(false);
  readonly resetGames = signal(true);
  readonly resetStrategyProfiles = signal(false);
  readonly resetPairings = signal(true);
  readonly resetDates = signal(false);
  readonly resetFieldTimeslots = signal(false);
  readonly resetConfirmText = signal('');

  readonly anyResetChecked = computed(() =>
    this.resetGames() || this.resetStrategyProfiles() || this.resetPairings()
    || this.resetDates() || this.resetFieldTimeslots()
  );

  selectTab(key: ScheduleConfigTab): void {
    this.activeTab.set(key);
  }

  // ── Reset dialog ──

  openResetDialog(): void {
    this.resetGames.set(true);
    this.resetStrategyProfiles.set(false);
    this.resetPairings.set(true);
    this.resetDates.set(false);
    this.resetFieldTimeslots.set(false);
    this.resetConfirmText.set('');
    this.showResetDialog.set(true);
  }

  onResetCancelled(): void {
    this.showResetDialog.set(false);
  }

  onResetConfirmed(): void {
    this.showResetDialog.set(false);
    this.resetConfirmed.emit({
      games: this.resetGames(),
      strategyProfiles: this.resetStrategyProfiles(),
      pairings: this.resetPairings(),
      dates: this.resetDates(),
      fieldTimeslots: this.resetFieldTimeslots(),
    });
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
