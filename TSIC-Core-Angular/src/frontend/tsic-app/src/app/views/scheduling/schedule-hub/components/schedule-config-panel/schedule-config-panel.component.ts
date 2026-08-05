import { Component, ChangeDetectionStrategy, computed, signal, output, inject, input, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobService } from '@infrastructure/services/job.service';
import type { AgegroupWithDivisionsDto } from '@core/api';
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
export type ScheduleTool = 'pools' | 'fields' | 'pairings' | 'timeslots' | 'bracket-seeds';

interface TabDef {
  key: ScheduleConfigTab;
  label: string;
  icon: string;
}

interface ToolDef {
  key: ScheduleTool;
  label: string;
  icon: string;
  champOnly?: boolean;
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

  /** Agegroup metadata from parent — passed to tabs so they don't re-fetch. */
  readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);

  /** Whether championship pairings exist (controls Bracket Seeds tool visibility). */
  readonly hasChampionshipPairings = input(false);

  /** Currently active tool (to highlight in dropdown). */
  readonly activeTool = input<ScheduleTool | null>(null);

  /** Expose cascade signal for template guard (defer tabs until loaded). */
  readonly cascade = this.cascadeSvc.cascade;

  // ── Tab ViewChild refs (only active tab is resolved at a time) ──
  private readonly datesTab = viewChild(DatesTabComponent);
  private readonly fieldsTab = viewChild(FieldsTabComponent);
  private readonly buildRulesTab = viewChild(BuildRulesTabComponent);
  private readonly roundsTab = viewChild(RoundsTabComponent);
  private readonly buildOrderTab = viewChild(BuildOrderTabComponent);
  private readonly gridTab = viewChild(GridTabComponent);

  // ── Derived state ──
  readonly eventName = computed(() => this.jobSvc.currentJob()?.jobName ?? '');

  // ── Outputs ──
  buildRequested = output<void>();
  resetConfirmed = output<DevResetOptions>();
  toolSelected = output<ScheduleTool>();

  /**
   * Hides the Tools menu and Reset from this header. ACCESS ONLY — every handler, output,
   * dialog and parent wiring below is intact, so flipping this to `true` puts both back
   * exactly as they were.
   *
   * Why they went (2026-08-04):
   *
   *   Tools  duplicated the Scheduling Checklist. Pool Assignment, Manage Pairings and Manage
   *          Timeslots ARE steps 1-3, and reaching them from inside the build screen invites a
   *          scheduler to reconfigure the inputs while looking at the output of the last build.
   *          The checklist is the front door; this was a second one opening onto the same rooms.
   *
   *   Reset  is not a config reset. It emits games + dates + fieldTimeslots (see
   *          onResetConfirmed) — one button that throws away steps 3 and 4 of the checklist.
   *          Far too destructive to sit one click from a schedule, in a red outline, next to
   *          a tools menu.
   *
   * Note this is the only entry point to `activeTool`, so the hub's embedded-tool mode is now
   * unreachable. That is intended: each of those tools has its own page off the checklist.
   */
  readonly showAdvancedHeaderActions = false;

  // ── Tools dropdown state ──
  readonly toolsDropdownOpen = signal(false);
  readonly tools: ToolDef[] = [
    { key: 'pools', label: 'Pool Assignment', icon: 'bi-collection' },
    { key: 'fields', label: 'Manage Fields', icon: 'bi-grid-3x3' },
    { key: 'pairings', label: 'Manage Pairings', icon: 'bi-people' },
    { key: 'timeslots', label: 'Manage Timeslots', icon: 'bi-clock' },
    { key: 'bracket-seeds', label: 'Bracket Seeds', icon: 'bi-trophy', champOnly: true },
  ];

  readonly visibleTools = computed(() =>
    this.tools.filter(t => !t.champOnly || this.hasChampionshipPairings())
  );

  toggleToolsDropdown(): void {
    this.toolsDropdownOpen.set(!this.toolsDropdownOpen());
  }

  selectTool(tool: ScheduleTool): void {
    this.toolsDropdownOpen.set(false);
    this.toolSelected.emit(tool);
  }

  closeToolsDropdown(): void {
    this.toolsDropdownOpen.set(false);
  }

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
      case 'dates': this.datesTab()?.reload(); break;
      case 'fields': this.fieldsTab()?.reload(); break;
      case 'buildRules': this.buildRulesTab()?.reload(); break;
      case 'rounds': this.roundsTab()?.reload(); break;
      case 'buildOrder': this.buildOrderTab()?.reload(); break;
      case 'grid': this.gridTab()?.reload(); break;
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
