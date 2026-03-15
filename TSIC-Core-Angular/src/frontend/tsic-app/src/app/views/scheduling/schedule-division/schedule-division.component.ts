import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import {
    ScheduleDivisionService,
    type AutoScheduleResponse,
    type AgegroupWithDivisionsDto,
    type DivisionSummaryDto,
    type GameDateInfoDto,
    type PairingDto,
    type DivisionPairingsResponse,
    type DivisionTeamDto,
    type ScheduleGridResponse,
    type ScheduleGridRow,
    type ScheduleFieldColumn,
    type ScheduleGameDto
} from './services/schedule-division.service';
import { Observable } from 'rxjs';
import { AutoBuildService } from '../auto-build/services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';
import { TimeslotService } from '../timeslots/services/timeslot.service';
import { formatTime, teamDes, contrastText, agTeamCount } from '../shared/utils/scheduling-helpers';
import { findTimeClashInRow } from '../shared/utils/conflict-detection';
import type { ScheduleScope } from '../shared/utils/scheduling-helpers';
import { DivisionNavigatorComponent } from '../shared/components/division-navigator/division-navigator.component';
import { ScheduleGridComponent } from '../shared/components/schedule-grid/schedule-grid.component';
import { OperationSpinnerModalComponent } from '../shared/components/operation-spinner-modal/operation-spinner-modal.component';
import { PairingsPanelComponent } from './components/pairings-panel/pairings-panel.component';
import type { DevResetOptions } from './components/schedule-config/schedule-config.types';
import { AutoScheduleConfigModalComponent, type AutoScheduleBuildEvent } from './components/auto-schedule-config-modal/auto-schedule-config-modal.component';
import { CanvasConfigPanelComponent } from './components/canvas-config-panel/canvas-config-panel.component';
import { BuildResultsPanelComponent } from './components/build-results-panel/build-results-panel.component';
import { BulkDateAssignModalComponent } from './components/bulk-date-assign-modal/bulk-date-assign-modal.component';
import { ScheduleConfigPanelComponent } from './components/schedule-config-panel/schedule-config-panel.component';
import { ManageFieldsComponent } from '../fields/manage-fields.component';
import { ManagePairingsComponent } from '../pairings/manage-pairings.component';
import { ManageTimeslotsComponent } from '../timeslots/manage-timeslots.component';
import { PoolAssignmentComponent } from '../../ladt/pool-assignment/pool-assignment.component';
import { MasterScheduleComponent } from '../master-schedule/master-schedule.component';
import { QaResultsComponent } from '../qa-results/qa-results.component';
import { ReschedulerComponent } from '../rescheduler/rescheduler.component';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { GameSummaryResponse, DivisionStrategyEntry, AutoBuildResult, AutoBuildQaResult, AgegroupBuildEntry } from '@core/api';
import { ScheduleConfigService } from './components/schedule-config/schedule-config.service';
import { ScheduleCascadeService } from './components/schedule-config/schedule-cascade.service';
import type { CanvasReadinessResponse } from '@core/api';
import { LadtService } from '../../ladt/editor/services/ladt.service';

@Component({
    selector: 'app-schedule-division',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent, DivisionNavigatorComponent, ScheduleGridComponent, OperationSpinnerModalComponent, PairingsPanelComponent, AutoScheduleConfigModalComponent, CanvasConfigPanelComponent, BuildResultsPanelComponent, BulkDateAssignModalComponent, ScheduleConfigPanelComponent, ManageFieldsComponent, ManagePairingsComponent, ManageTimeslotsComponent, PoolAssignmentComponent, MasterScheduleComponent, QaResultsComponent, ReschedulerComponent],
    templateUrl: './schedule-division.component.html',
    styleUrl: './schedule-division.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
    providers: [ScheduleConfigService]
})
export class ScheduleDivisionComponent implements OnInit {
    private readonly svc = inject(ScheduleDivisionService);
    private readonly autoBuildSvc = inject(AutoBuildService);
    private readonly qaSvc = inject(ScheduleQaService);
    private readonly timeslotSvc = inject(TimeslotService);
    private readonly jobSvc = inject(JobService);
    private readonly toast = inject(ToastService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly auth = inject(AuthService);
    readonly configSvc = inject(ScheduleConfigService);
    readonly cascadeSvc = inject(ScheduleCascadeService);
    private readonly ladtSvc = inject(LadtService);

    readonly isSuperUser = this.auth.isSuperuser;

    // ── Config service init tracking ──
    private configInitDone = false;
    private fullReadinessResponse: CanvasReadinessResponse | null = null;
    private agegroupsLoaded = false;

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;
    @ViewChild('rapidFieldInput') rapidFieldInputEl?: ElementRef<HTMLInputElement>;
    @ViewChild(ScheduleConfigPanelComponent) configPanel?: ScheduleConfigPanelComponent;
    @ViewChild(DivisionNavigatorComponent) navigator?: DivisionNavigatorComponent;

    // ── Hub mode ──
    readonly mode = signal<'configure' | 'schedule' | 'master' | 'qa' | 'reschedule'>('configure');
    readonly activeTool = signal<'fields' | 'pairings' | 'timeslots' | 'pools' | null>(null);
    readonly showToolsSection = signal(true);

    // ── Scope selection model (replaces separate selectedDivision + selectedAgegroupId) ──
    readonly scope = signal<ScheduleScope>({ level: 'event' });

    // Derived for backward compat (grid, pairings, etc. still use these)
    readonly selectedDivision = computed<DivisionSummaryDto | null>(() => {
        const s = this.scope();
        if (s.level !== 'division') return null;
        const ag = this.agegroups().find(a => a.agegroupId === s.agegroupId);
        return ag?.divisions.find(d => d.divId === s.divId) ?? null;
    });
    readonly selectedAgegroupId = computed<string | null>(() => {
        const s = this.scope();
        return s.level === 'event' ? null : s.agegroupId;
    });

    // ── Navigator state ──
    readonly agegroups = signal<AgegroupWithDivisionsDto[]>([]);
    readonly isNavLoading = signal(false);
    readonly canvasReadiness = signal<Record<string, import('@core/api').AgegroupCanvasReadinessDto>>({});
    readonly assignedFieldCount = signal(0);
    readonly priorYearDefaults = signal<import('@core/api').PriorYearFieldDefaults | null>(null);
    readonly priorYearRounds = signal<Record<string, number> | null>(null);
    readonly strategyProfiles = signal<DivisionStrategyEntry[]>([]);
    readonly strategySource = signal<string>('defaults');
    readonly isSavingStrategy = signal(false);

    /** Auto-detected event type label for the stepper badge. */
    readonly eventTypeLabel = computed(() => {
        const job = this.jobSvc.currentJob();
        const typeName = (job?.jobTypeName ?? '').toLowerCase();
        if (typeName.includes('league')) return 'League';
        if (typeName.includes('tournament')) return 'Tournament';
        return 'Tournament';
    });

    // ── Game Summary (from auto-build service) ──
    readonly gameSummary = signal<GameSummaryResponse | null>(null);

    readonly scopeGameCount = computed(() => {
        const summary = this.gameSummary();
        if (!summary) return 0;
        const s = this.scope();
        switch (s.level) {
            case 'event': return summary.totalGames;
            case 'agegroup': return summary.divisions
                .filter(d => d.agegroupId === s.agegroupId)
                .reduce((sum, d) => sum + d.gameCount, 0);
            case 'division': return summary.divisions
                .find(d => d.divId === s.divId)?.gameCount ?? 0;
        }
    });
    readonly hasGamesInScope = computed(() => this.scopeGameCount() > 0);

    readonly scopeLabel = computed(() => {
        const s = this.scope();
        switch (s.level) {
            case 'event': return 'Entire';
            case 'agegroup': return this.selectedAgegroup()?.agegroupName ?? '';
            case 'division': {
                const ag = this.selectedAgegroup()?.agegroupName ?? '';
                const div = this.selectedDivision()?.divName ?? '';
                return `${ag}:${div}`;
            }
        }
    });

    /** Breadcrumb segments for the right panel. Only shown when drilled below event level. */
    readonly breadcrumbs = computed((): { label: string; level: 'event' | 'agegroup' }[] => {
        const s = this.scope();
        if (s.level === 'event') return [];

        const eventName = this.gameSummary()?.jobName ?? 'Event';
        const crumbs: { label: string; level: 'event' | 'agegroup' }[] = [
            { label: eventName, level: 'event' }
        ];

        if (s.level === 'division') {
            crumbs.push({ label: this.selectedAgegroup()?.agegroupName ?? '', level: 'agegroup' });
        }

        return crumbs;
    });

    /** The final (non-clickable) breadcrumb label — current scope. */
    readonly breadcrumbCurrent = computed((): string => {
        const s = this.scope();
        if (s.level === 'agegroup') return this.selectedAgegroup()?.agegroupName ?? '';
        if (s.level === 'division') return this.selectedDivision()?.divName ?? '';
        return '';
    });

    // ── Breadcrumb dropdown navigator ──
    readonly openCrumbDropdown = signal<'agegroup' | 'division' | null>(null);

    readonly crumbAgegroupOptions = computed(() => {
        const currentAgId = this.selectedAgegroupId();
        return this.agegroups().map(ag => ({
            agegroupId: ag.agegroupId,
            name: ag.agegroupName,
            color: ag.color ?? null,
            teamCount: ag.divisions.reduce((sum, d) => sum + (d.teamCount ?? 0), 0),
            active: ag.agegroupId === currentAgId,
        }));
    });

    readonly crumbDivisionOptions = computed(() => {
        const ag = this.selectedAgegroup();
        if (!ag) return [];
        const currentDivId = this.scope().level === 'division' ? (this.scope() as any).divId : null;
        return ag.divisions.map(d => ({
            divId: d.divId,
            name: d.divName,
            teamCount: d.teamCount ?? 0,
            active: d.divId === currentDivId,
        }));
    });

    toggleCrumbDropdown(level: 'agegroup' | 'division'): void {
        this.openCrumbDropdown.set(this.openCrumbDropdown() === level ? null : level);
    }

    selectCrumbAgegroup(agId: string): void {
        this.openCrumbDropdown.set(null);
        // Expand in tree and navigate
        this.navigator?.expandedAgegroups.set(new Set([agId]));
        this.onAgegroupSelected({ agegroupId: agId });
    }

    selectCrumbDivision(divId: string): void {
        this.openCrumbDropdown.set(null);
        const agId = this.selectedAgegroupId();
        if (!agId) return;
        const ag = this.agegroups().find(a => a.agegroupId === agId);
        const div = ag?.divisions.find(d => d.divId === divId);
        if (!div) return;
        // Expand in tree and navigate
        this.navigator?.expandedAgegroups.set(new Set([agId]));
        this.onDivisionSelected({ division: div, agegroupId: agId });
    }

    closeCrumbDropdowns(): void {
        this.openCrumbDropdown.set(null);
    }

    // ── Pairings state ──
    readonly divisionResponse = signal<DivisionPairingsResponse | null>(null);
    readonly pairings = signal<PairingDto[]>([]);
    readonly isPairingsLoading = signal(false);

    // ── Teams state ──
    readonly divisionTeams = signal<DivisionTeamDto[]>([]);
    readonly editingTeam = signal<{ teamId: string; divRank: number; teamName: string; clubName: string } | null>(null);
    readonly isSavingTeam = signal(false);

    // ── Who Plays Who ──
    readonly whoPlaysWhoMatrix = signal<number[][] | null>(null);

    // ── Schedule Grid state ──
    readonly gridResponse = signal<ScheduleGridResponse | null>(null);
    readonly isGridLoading = signal(false);
    readonly highlightGameGid = signal<number | null>(null);
    readonly highlightAllDiv = signal(false);
    private highlightAllDivTimer: ReturnType<typeof setTimeout> | null = null;

    // ── Placement workflow ──
    readonly placementMode = signal<'mouse' | 'keyboard'>(this.loadPlacementMode());
    readonly selectedPairing = signal<PairingDto | null>(null);
    readonly isPlacing = signal(false);

    // ── Auto-schedule (division-level) ──
    readonly isAutoScheduling = signal(false);
    readonly autoScheduleResult = signal<AutoScheduleResponse | null>(null);
    readonly showAutoScheduleConfirm = signal(false);

    // ── Delete confirmation ──
    readonly showDeleteConfirm = signal(false);
    readonly isDeletingGames = signal(false);
    readonly deleteConfirmText = signal('');

    // ── Dev reset ──
    readonly isResetting = signal(false);

    // ── Auto-schedule config modal ──
    readonly showAutoScheduleModal = signal(false);
    readonly showEventBuildConfirm = signal(false);
    readonly modalGameDates = signal<GameDateInfoDto[]>([]);
    readonly isExecuting = signal(false);

    // ── Build results ──
    readonly buildResult = signal<AutoBuildResult | null>(null);
    readonly qaResult = signal<AutoBuildQaResult | null>(null);
    readonly qaLoading = signal(false);
    readonly buildElapsedMs = signal(0);
    readonly showBuildResults = signal(false);
    readonly isUndoing = signal(false);

    // ── Operation spinner modal ──
    readonly showOperationModal = signal(false);
    readonly operationTitle = signal('');
    readonly operationSubtitle = signal('');
    readonly operationIcon = signal('bi-lightning-charge-fill');

    // ── Prerequisite check ──
    readonly prerequisiteErrors = signal<string[]>([]);
    readonly isCheckingPrereqs = signal(false);
    readonly missingPairingTCnts = signal<number[]>([]);
    readonly existingPairingRounds = signal<Record<number, number>>({});
    readonly isGeneratingPairings = signal(false);

    // ── Bulk date assignment modal ──
    readonly showBulkDateModal = signal(false);

    // ── Computed helpers ──
    readonly gridColumns = computed(() => this.gridResponse()?.columns ?? []);
    readonly gridRows = computed(() => this.gridResponse()?.rows ?? []);

    readonly selectedAgegroup = computed(() => {
        const agId = this.selectedAgegroupId();
        if (!agId) return null;
        return this.agegroups().find(ag => ag.agegroupId === agId) ?? null;
    });
    readonly selectedAgegroupName = computed(() => this.selectedAgegroup()?.agegroupName ?? '');
    readonly selectedAgegroupColor = computed(() => this.selectedAgegroup()?.color ?? null);
    readonly selectedAgegroupTextColor = computed(() => contrastText(this.selectedAgegroupColor()));
    readonly teamCount = computed(() => this.divisionResponse()?.teamCount ?? 0);
    readonly rankOptions = computed(() => Array.from({ length: this.divisionTeams().length }, (_, i) => i + 1));
    readonly allPairingsScheduled = computed(() => this.pairings().length > 0 && this.pairings().every(p => !p.bAvailable));
    readonly remainingPairingsCount = computed(() => this.pairings().filter(p => p.bAvailable).length);
    readonly isAgegroupConfigured = computed(() => {
        const s = this.scope();
        if (s.level === 'event') return true;
        const r = this.canvasReadiness()[s.agegroupId];
        return r?.isConfigured ?? false;
    });

    // ── Rapid-placement modal ──

    readonly showRapidModal = signal(false);
    readonly rapidPairing = signal<PairingDto | null>(null);
    readonly rapidFieldFilter = signal('');
    readonly rapidTimeFilter = signal('');
    readonly rapidFieldIndex = signal(-1);
    readonly rapidTimeIndex = signal(-1);
    readonly rapidFieldOpen = signal(false);
    readonly rapidTimeOpen = signal(false);
    readonly rapidSelectedField = signal<ScheduleFieldColumn | null>(null);
    readonly rapidSelectedTime = signal<{ gDate: string; label: string; rowIndex: number } | null>(null);

    readonly rapidFieldsFiltered = computed(() => {
        const filter = this.rapidFieldFilter().toLowerCase();
        const fields = this.gridColumns();
        if (!filter) return fields;
        return fields.filter(f => f.fName.toLowerCase().includes(filter));
    });

    readonly rapidOpenSlots = computed(() => {
        const rows = this.gridRows();
        const fields = this.gridColumns();
        const selectedFieldId = this.rapidSelectedField()?.fieldId;
        if (!selectedFieldId) return [];

        const colIdx = fields.findIndex(f => f.fieldId === selectedFieldId);
        if (colIdx < 0) return [];

        return rows
            .map((r, i) => ({ row: r, rowIndex: i }))
            .filter(({ row }) => !row.cells[colIdx])
            .map(({ row, rowIndex }) => ({
                gDate: row.gDate,
                label: this.formatTime(row.gDate),
                rowIndex
            }));
    });

    readonly rapidTimesFiltered = computed(() => {
        const filter = this.rapidTimeFilter().toLowerCase();
        const slots = this.rapidOpenSlots();
        if (!filter) return slots;
        return slots.filter(s => s.label.toLowerCase().includes(filter));
    });

    ngOnInit(): void {
        this.loadAgegroups();
        this.refreshGameSummary();
        this.loadCanvasReadiness();
        this.loadStrategyProfiles();
        this.checkPairingStatus();
        // Default mode is 'schedule' at event scope — load full event grid
        if (this.mode() === 'schedule') {
            this.loadEventGrid();
        }
    }

    // ── Navigator ──

    loadAgegroups(): void {
        this.isNavLoading.set(true);
        this.svc.getAgegroups().subscribe({
            next: (data) => {
                const filtered = data
                    .filter(ag => {
                        const name = (ag.agegroupName ?? '').toUpperCase();
                        return name !== 'DROPPED TEAMS' && !name.startsWith('WAITLIST');
                    })
                    .map(ag => ({
                        ...ag,
                        divisions: ag.divisions.filter(d =>
                            (d.divName ?? '').toUpperCase() !== 'UNASSIGNED'
                        )
                    }))
                    .filter(ag => ag.divisions.length > 0)
                    .sort((a, b) => (a.agegroupName ?? '').localeCompare(b.agegroupName ?? ''));
                this.agegroups.set(filtered);
                this.isNavLoading.set(false);
                this.agegroupsLoaded = true;
                this.tryInitConfig();
            },
            error: () => this.isNavLoading.set(false)
        });
    }

    refreshGameSummary(): void {
        this.autoBuildSvc.getGameSummary().subscribe({
            next: (summary) => this.gameSummary.set(summary),
            error: () => this.gameSummary.set(null)
        });
    }

    loadCanvasReadiness(): void {
        this.timeslotSvc.getReadiness().subscribe({
            next: (res) => {
                const map: Record<string, import('@core/api').AgegroupCanvasReadinessDto> = {};
                for (const ag of res.agegroups) {
                    map[ag.agegroupId] = ag;
                }
                this.canvasReadiness.set(map);
                this.assignedFieldCount.set(res.assignedFieldCount);
                this.priorYearDefaults.set(res.priorYearDefaults ?? null);
                this.priorYearRounds.set(res.priorYearRounds ?? null);
                this.fullReadinessResponse = res;
                this.tryInitConfig();

                // Proactive auto-seed: if prior job exists and fields are unconfigured,
                // copy FLS rows + apply timing pattern from source
                this.tryAutoSeedFromSource(res);
            },
            error: () => {
                this.canvasReadiness.set({});
                this.assignedFieldCount.set(0);
                this.priorYearDefaults.set(null);
                this.priorYearRounds.set(null);
            }
        });
    }

    private autoSeedAttempted = false;

    private tryAutoSeedFromSource(res: import('@core/api').CanvasReadinessResponse): void {
        if (this.autoSeedAttempted) return;

        const priorJobId = res.priorYearDefaults?.priorJobId;
        if (!priorJobId) return;

        const noFls = res.assignedFieldCount === 0;
        const unconfiguredFields = res.agegroups.filter(ag => ag.fieldCount === 0);
        const unconfiguredDates = res.agegroups.filter(ag => ag.dateCount === 0);
        if (!noFls && unconfiguredFields.length === 0 && unconfiguredDates.length === 0) return;

        this.autoSeedAttempted = true;

        this.timeslotSvc.autoSeedFromSource(priorJobId).subscribe({
            next: (result) => {
                const didAnything = result.agegroupsSeeded > 0
                    || result.fieldsLeagueSeasonCopied
                    || (result.datesSeeded ?? 0) > 0;
                if (!didAnything) return;

                const parts: string[] = [];
                if (result.fieldsLeagueSeasonCopied) parts.push('field assignments copied');
                if (result.agegroupsSeeded > 0)
                    parts.push(`${result.agegroupsSeeded} agegroup(s) field config seeded`);
                if ((result.datesSeeded ?? 0) > 0)
                    parts.push(`${result.datesSeeded} agegroup(s) dates seeded`);

                this.toast.show(
                    `Auto-configured from prior year: ${parts.join(', ')}`,
                    'success', 6000
                );
                this.loadCanvasReadiness();
            },
            error: () => {
                // Silent failure — director can configure manually
            }
        });
    }

    loadStrategyProfiles(): void {
        // Load via cascade service (replaces old strategy-profiles endpoint for display)
        this.cascadeSvc.loadCascade().subscribe({
            next: () => {
                // Derive strategy entries from cascade for backward-compat display
                this.strategyProfiles.set(this.cascadeSvc.getStrategyEntries() as DivisionStrategyEntry[]);
                this.strategySource.set('saved');

                // Auto-seed waves from prior year projection if cascade has no waves
                this.trySeedWavesFromProjection();

                // Explicitly reload the active config tab (replaces effect()-based cascade watching)
                this.configPanel?.reloadActiveTab();
            },
            error: () => {
                // No cascade data — load from legacy endpoint as fallback
                this.autoBuildSvc.getStrategyProfiles().subscribe({
                    next: (res) => {
                        this.strategyProfiles.set(res.strategies);
                        this.strategySource.set(res.source);
                    },
                    error: () => {
                        this.strategyProfiles.set([]);
                        this.strategySource.set('defaults');
                    }
                });
            }
        });
    }

    /**
     * If the cascade DB has no wave assignments but a prior year exists,
     * fetch the projected config and seed the cascade with suggested waves.
     * This bridges the gap between projection (computed) and cascade (persisted).
     */
    private trySeedWavesFromProjection(): void {
        if (!this.cascadeSvc.hasNoWaves()) return;

        const py = this.priorYearDefaults();
        if (!py?.priorJobId) return;

        this.autoBuildSvc.getProjectedConfig(py.priorJobId).subscribe({
            next: (projected) => {
                const divWaves = projected.suggestedDivisionWaves as Record<string, number> | null;
                if (!divWaves || Object.keys(divWaves).length === 0) return;

                // Build agegroupDates: agegroupId → list of ISO date strings
                const agegroupDates: Record<string, string[]> = {};
                for (const ag of projected.agegroups) {
                    agegroupDates[ag.agegroupId] = ag.gameDays.map(gd => gd.date);
                }

                this.cascadeSvc.seedWaves({ divisionWaves: divWaves, agegroupDates }).subscribe({
                    next: () => {
                        // Refresh strategy entries with new wave data
                        this.strategyProfiles.set(
                            this.cascadeSvc.getStrategyEntries() as DivisionStrategyEntry[]
                        );
                    }
                });
            }
        });
    }

    /**
     * Try to initialize the ScheduleConfigService once both
     * parallel loads (readiness + agegroups) are done.
     * Strategies/waves now come from the cascade service, not from config.
     */
    private tryInitConfig(): void {
        if (this.configInitDone) return;
        if (!this.fullReadinessResponse || !this.agegroupsLoaded) return;

        this.configInitDone = true;
        this.configSvc.initialize(
            this.fullReadinessResponse,
            this.agegroups().map(ag => ({
                agegroupId: ag.agegroupId,
                agegroupName: ag.agegroupName,
                teamCount: agTeamCount(ag),
                divisions: ag.divisions.map(d => ({ divId: d.divId, divName: d.divName }))
            }))
        );
    }

    onCanvasConfigured(): void {
        this.loadCanvasReadiness();
        // If at agegroup level, reload the grid now that canvas is configured
        const s = this.scope();
        if (s.level === 'agegroup') {
            const ag = this.agegroups().find(a => a.agegroupId === s.agegroupId);
            if (ag?.divisions.length) {
                this.loadScheduleGrid(ag.divisions[0].divId, s.agegroupId);
            }
        }
    }

    // ── Bulk date assignment modal ──

    // ── Bulk date modal ──

    openBulkDateModal(): void {
        this.showBulkDateModal.set(true);
    }

    onBulkDateApplied(): void {
        this.loadCanvasReadiness();
    }

    closeBulkDateModal(): void {
        this.showBulkDateModal.set(false);
    }

    // ── Hub mode switching ──

    setMode(mode: 'configure' | 'schedule' | 'master' | 'qa' | 'reschedule'): void {
        this.activeTool.set(null);
        this.mode.set(mode);

        // When entering Schedule mode, reset to event scope to show the full schedule.
        // User drills into specific divisions manually via the navigator.
        if (mode === 'schedule') {
            this.onEventSelected();
        }
    }

    setTool(tool: 'fields' | 'pairings' | 'timeslots' | 'pools'): void {
        this.activeTool.set(this.activeTool() === tool ? null : tool);
    }

    // ── Scope selection handlers (wired from navigator outputs) ──

    onEventSelected(): void {
        this.scope.set({ level: 'event' });
        this.dismissBuildResults();
        this.clearDivisionState();
        // Load full event grid when in Schedule mode
        if (this.mode() === 'schedule') {
            this.loadEventGrid();
        }
    }

    loadEventGrid(): void {
        if (!this.gridResponse()) {
            this.isGridLoading.set(true);
        }
        this.svc.getEventGrid().subscribe({
            next: (grid) => {
                this.gridResponse.set(grid);
                this.isGridLoading.set(false);
            },
            error: () => {
                this.gridResponse.set(null);
                this.isGridLoading.set(false);
            }
        });
    }

    onBreadcrumbClick(level: 'event' | 'agegroup'): void {
        if (level === 'event') {
            this.onEventSelected();
        } else {
            const agId = this.selectedAgegroupId();
            if (agId) this.onAgegroupSelected({ agegroupId: agId });
        }
    }

    onAgegroupSelected(event: { agegroupId: string }): void {
        this.scope.set({ level: 'agegroup', agegroupId: event.agegroupId });
        this.dismissBuildResults();
        this.clearDivisionState();
        // Load first division's grid for scroll context
        const ag = this.agegroups().find(a => a.agegroupId === event.agegroupId);
        if (ag?.divisions.length) {
            this.loadScheduleGrid(ag.divisions[0].divId, event.agegroupId);
        }
    }

    onDivisionSelected(event: { division: DivisionSummaryDto; agegroupId: string }): void {
        this.scope.set({ level: 'division', agegroupId: event.agegroupId, divId: event.division.divId });
        this.dismissBuildResults();
        this.selectedPairing.set(null);
        this.selectedGame.set(null);
        this.highlightGameGid.set(null);
        this.showDeleteConfirm.set(false);
        this.loadDivisionData(event.division.divId, event.agegroupId);
    }

    onAgegroupColorChanged(event: { agegroupId: string; color: string | null }): void {
        this.ladtSvc.updateAgegroupColor(event.agegroupId, event.color).subscribe({
            next: () => {
                // Update local agegroups signal with new color
                this.agegroups.set(this.agegroups().map(ag =>
                    ag.agegroupId === event.agegroupId
                        ? { ...ag, color: event.color }
                        : ag
                ));
                this.toast.show('Color updated', 'success');
            },
            error: () => {
                this.toast.show('Failed to update color', 'danger');
            }
        });
    }

    private dismissBuildResults(): void {
        if (this.showBuildResults()) {
            this.showBuildResults.set(false);
            this.buildResult.set(null);
            this.qaResult.set(null);
        }
    }

    private clearDivisionState(): void {
        this.selectedPairing.set(null);
        this.selectedGame.set(null);
        this.highlightGameGid.set(null);
        this.showDeleteConfirm.set(false);
        this.divisionResponse.set(null);
        this.pairings.set([]);
        this.divisionTeams.set([]);
        this.whoPlaysWhoMatrix.set(null);
    }

    private loadDivisionData(divId: string, agegroupId: string): void {
        this.loadDivisionPairings(divId);
        this.loadDivisionTeams(divId);
        this.loadScheduleGrid(divId, agegroupId);
    }

    // ── Pairings ──

    loadDivisionPairings(divId: string): void {
        if (this.pairings().length === 0) {
            this.isPairingsLoading.set(true);
        }
        this.svc.getDivisionPairings(divId).subscribe({
            next: (resp) => {
                this.divisionResponse.set(resp);
                this.pairings.set(resp.pairings);
                this.isPairingsLoading.set(false);
                if (resp.teamCount > 0) {
                    this.svc.getWhoPlaysWho(resp.teamCount).subscribe({
                        next: (wpw) => this.whoPlaysWhoMatrix.set(wpw.matrix)
                    });
                } else {
                    this.whoPlaysWhoMatrix.set(null);
                }
            },
            error: () => this.isPairingsLoading.set(false)
        });
    }

    loadDivisionTeams(divId: string): void {
        this.svc.getDivisionTeams(divId).subscribe({
            next: (teams) => this.divisionTeams.set(teams),
            error: () => this.divisionTeams.set([])
        });
    }

    // ── Team Editing (modal) ──

    openTeamEditModal(team: DivisionTeamDto): void {
        this.editingTeam.set({
            teamId: team.teamId,
            divRank: team.divRank,
            teamName: team.teamName ?? '',
            clubName: team.clubName ?? ''
        });
    }

    closeTeamEditModal(): void {
        this.editingTeam.set(null);
    }

    updateEditingRank(rank: number): void {
        const t = this.editingTeam();
        if (t) this.editingTeam.set({ ...t, divRank: rank });
    }

    updateEditingName(name: string): void {
        const t = this.editingTeam();
        if (t) this.editingTeam.set({ ...t, teamName: name });
    }

    saveTeamEdit(): void {
        const team = this.editingTeam();
        if (!team) return;
        this.isSavingTeam.set(true);
        this.svc.editDivisionTeam({
            teamId: team.teamId,
            divRank: team.divRank,
            teamName: team.teamName
        }).subscribe({
            next: (updatedTeams) => {
                this.divisionTeams.set(updatedTeams);
                this.editingTeam.set(null);
                this.isSavingTeam.set(false);
                const div = this.selectedDivision();
                const agId = this.selectedAgegroupId();
                if (div && agId) this.loadScheduleGrid(div.divId, agId);
            },
            error: () => this.isSavingTeam.set(false)
        });
    }

    // ── Schedule Grid ──

    loadScheduleGrid(divId: string, agegroupId: string, highlightGid?: number): void {
        if (!this.gridResponse()) {
            this.isGridLoading.set(true);
        }
        this.svc.getScheduleGrid(divId, agegroupId).subscribe({
            next: (grid) => {
                this.gridResponse.set(grid);
                this.isGridLoading.set(false);

                if (highlightGid != null) {
                    // Post-move: scroll to moved game + temporary highlight
                    this.highlightGameGid.set(highlightGid);
                    setTimeout(() => this.scheduleGrid?.scrollToGame(highlightGid));
                    setTimeout(() => this.highlightGameGid.set(null), 3000);
                } else {
                    // Normal load: scroll to first div game + flash division
                    const gid = this.findFirstGameGidInGrid(grid, divId);
                    this.highlightGameGid.set(null);
                    this.flashAllDiv();
                    setTimeout(() => {
                        if (gid) {
                            this.scheduleGrid?.scrollToGame(gid);
                        } else {
                            this.scheduleGrid?.scrollToFirstRelevant(divId);
                        }
                    });
                }
            },
            error: () => {
                this.gridResponse.set(null);
                this.isGridLoading.set(false);
            }
        });
    }

    // ── Placement Workflow ──

    private loadPlacementMode(): 'mouse' | 'keyboard' {
        const stored = localStorage.getItem(LocalStorageKey.SchedulePlacementMode);
        return stored === 'keyboard' ? 'keyboard' : 'mouse';
    }

    setPlacementMode(mode: 'mouse' | 'keyboard'): void {
        this.placementMode.set(mode);
        localStorage.setItem(LocalStorageKey.SchedulePlacementMode, mode);
    }

    onPairingClick(pairing: PairingDto): void {
        if (this.placementMode() === 'keyboard') {
            this.openRapidModalFor(pairing);
        } else {
            this.selectPairingForPlacement(pairing);
        }
    }

    selectPairingForPlacement(pairing: PairingDto): void {
        this.selectedGame.set(null);
        if (this.selectedPairing()?.ai === pairing.ai) {
            this.selectedPairing.set(null);
        } else {
            this.selectedPairing.set(pairing);
            this.scheduleGrid?.scrollToNextOpenSlot(0);
        }
    }

    locateScheduledGame(pairing: PairingDto): void {
        const gid = this.findGidForPairing(pairing);
        if (!gid) return;
        this.highlightAllDiv.set(false);
        this.highlightGameGid.set(gid);
        this.scheduleGrid?.scrollToGame(gid);
    }

    private findGidForPairing(pairing: PairingDto): number | null {
        const divId = this.selectedDivision()?.divId;
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (!cell || cell.divId !== divId) continue;
                if (cell.rnd === pairing.rnd
                    && cell.t1No === pairing.t1
                    && cell.t2No === pairing.t2
                    && cell.t1Type === pairing.t1Type
                    && cell.t2Type === pairing.t2Type) {
                    return cell.gid;
                }
            }
        }
        return null;
    }

    placeGame(row: ScheduleGridRow, colIndex: number): void {
        const pairing = this.selectedPairing();
        const div = this.selectedDivision();
        const agId = this.selectedAgegroupId();
        if (!pairing || !div || !agId) return;

        const column = this.gridColumns()[colIndex];
        if (!column) return;

        const bracketBlock = this.checkBracketPlacement(pairing);
        if (bracketBlock) {
            this.toast.show(bracketBlock, 'danger', 5000);
            return;
        }

        const teamIds = this.resolvePairingTeamIds(pairing);
        const clash = this.findClashInRow(row, teamIds);
        if (clash) {
            this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
            return;
        }

        this.isPlacing.set(true);
        this.svc.placeGame({
            pairingAi: pairing.ai,
            gDate: row.gDate,
            fieldId: column.fieldId,
            agegroupId: agId,
            divId: div.divId
        }).subscribe({
            next: (game) => {
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === row.gDate) {
                            const updatedCells = [...r.cells];
                            updatedCells[colIndex] = game;
                            return { ...r, cells: updatedCells };
                        }
                        return r;
                    });
                    return { ...grid, rows: updatedRows };
                });
                this.pairings.update(list =>
                    list.map(p => p.ai === pairing.ai ? { ...p, bAvailable: false } : p)
                );
                this.isPlacing.set(false);

                const nextPairing = this.pairings().find(p => p.ai !== pairing.ai && p.bAvailable);
                this.selectedPairing.set(nextPairing ?? null);

                if (nextPairing) {
                    const rows = this.gridRows();
                    const placedRowIdx = rows.findIndex(r => r.gDate === row.gDate);
                    this.scheduleGrid?.scrollToNextOpenSlot(placedRowIdx + 1);
                }

                this.refreshGameSummary();
            },
            error: () => this.isPlacing.set(false)
        });
    }

    // ── Delete single game ──

    deleteGame(game: ScheduleGameDto, row: ScheduleGridRow, colIndex: number): void {
        if (this.selectedGame()?.game.gid === game.gid) {
            this.selectedGame.set(null);
        }

        this.svc.deleteGame(game.gid).subscribe({
            next: () => {
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === row.gDate) {
                            const updatedCells = [...r.cells] as (ScheduleGameDto | null)[];
                            updatedCells[colIndex] = null;
                            return { ...r, cells: updatedCells as ScheduleGameDto[] };
                        }
                        return r;
                    });
                    return { ...grid, rows: updatedRows };
                });
                const div = this.selectedDivision();
                if (div) this.loadDivisionPairings(div.divId);
                this.refreshGameSummary();
            }
        });
    }

    // ── Three-tier delete ──

    confirmDeleteGames(): void {
        this.showDeleteConfirm.set(true);
    }

    cancelDeleteGames(): void {
        this.showDeleteConfirm.set(false);
        this.deleteConfirmText.set('');
    }

    executeDelete(): void {
        const s = this.scope();
        this.isDeletingGames.set(true);
        this.showDeleteConfirm.set(false);

        let delete$: Observable<unknown>;
        switch (s.level) {
            case 'division':
                delete$ = this.svc.deleteDivisionGames({ divId: s.divId });
                break;
            case 'agegroup':
                delete$ = this.svc.deleteAgegroupGames({ agegroupId: s.agegroupId });
                break;
            case 'event':
                delete$ = this.autoBuildSvc.undo();
                break;
        }

        delete$.subscribe({
            next: () => {
                this.isDeletingGames.set(false);
                this.deleteConfirmText.set('');
                this.refreshAfterBulkOperation();
                this.toast.show(`Deleted ${this.scopeLabel()} games`, 'success', 3000);
            },
            error: () => {
                this.isDeletingGames.set(false);
                this.deleteConfirmText.set('');
            }
        });
    }

    // ── Reset (clear scheduling data + optionally preconfigure from source) ──

    executeReset(options: DevResetOptions): void {
        this.isResetting.set(true);

        // Always preconfigure from prior year if available
        const sourceJobId = options.sourceJobId ?? this.priorYearDefaults()?.priorJobId ?? undefined;
        const hasSource = !!sourceJobId;
        const modalMsg = hasSource
            ? 'Clearing games… then preconfiguring from source'
            : 'Clearing games…';
        this.openOperationModal('Resetting Scheduling Data', modalMsg, 'bi-arrow-counterclockwise');
        this.autoBuildSvc.resetSchedule({
            games: options.games,
            strategyProfiles: options.strategyProfiles,
            pairings: options.pairings,
            dates: options.dates,
            fieldTimeslots: options.fieldTimeslots,
            fieldAssignments: false,
            sourceJobId
        }).subscribe({
            next: (result) => {
                this.showOperationModal.set(false);
                this.isResetting.set(false);
                const cleared: string[] = [];
                if (result.gamesDeleted) cleared.push(`${result.gamesDeleted} games deleted`);
                if (result.agegroupsCleared) cleared.push(`${result.agegroupsCleared} agegroups cleared`);
                if (result.pairingGroupsCleared) cleared.push(`${result.pairingGroupsCleared} pairing groups cleared`);

                const seeded: string[] = [];
                if (result.preconfig) {
                    const p = result.preconfig;
                    if (p.colorsApplied) seeded.push(`${p.colorsApplied} colors`);
                    if (p.datesSeeded) seeded.push(`${p.datesSeeded} ag dates`);
                    if (p.fieldAssignmentsSeeded) seeded.push(`${p.fieldAssignmentsSeeded} ag fields`);
                    if (p.fieldsLeagueSeasonCopied) seeded.push('field assignments copied from source');
                    if (p.pairingsGenerated?.length) seeded.push(`pairings for ${p.pairingsGenerated.join(', ')}-team`);
                    if (p.cascadeSeeded) seeded.push('build rules');
                }

                const parts: string[] = [];
                if (cleared.length) parts.push(cleared.join(', '));
                if (seeded.length) parts.push(`seeded: ${seeded.join(', ')}`);
                this.toast.show(
                    parts.length > 0 ? `Reset complete — ${parts.join('; ')}` : 'Reset complete — nothing to clear',
                    'success', 8000
                );

                this.configSvc.clearLocalStorage();
                this.configSvc.reset();
                this.configInitDone = false;

                this.refreshAfterBulkOperation();
                this.loadCanvasReadiness();
                this.loadStrategyProfiles();
                this.checkPairingStatus();

                // Force config panel to Dates tab (logical starting point after reset).
                // The tab switch creates a new DatesTabComponent whose ngOnInit() loads fresh data.
                this.configPanel?.selectTab('dates');
            },
            error: () => {
                this.showOperationModal.set(false);
                this.isResetting.set(false);
                this.toast.show('Reset failed', 'danger', 3000);
            }
        });
    }

    // ── Auto-schedule config modal ──

    openAutoScheduleConfig(): void {
        // Commit current config to localStorage before build
        this.configSvc.saveToLocalStorage();

        this.prerequisiteErrors.set([]);
        this.missingPairingTCnts.set([]);
        this.isCheckingPrereqs.set(true);

        this.autoBuildSvc.checkPrerequisites().subscribe({
            next: (result) => {
                if (result.allPassed) {
                    this.isCheckingPrereqs.set(false);
                    this.openAutoScheduleModal();
                    return;
                }

                // Hard blockers: pools or timeslots — these need manual intervention
                const hardErrors: string[] = [];
                if (!result.poolsAssigned) {
                    hardErrors.push(`${result.unassignedTeamCount} team${result.unassignedTeamCount > 1 ? 's' : ''} haven't been assigned to a pool yet`);
                }
                if (!result.timeslotsConfigured) {
                    hardErrors.push(`Timeslots not configured for: ${result.agegroupsMissingTimeslots.join(', ')}`);
                }

                if (hardErrors.length > 0) {
                    // Show hard errors; if pairings also missing, show that too
                    if (!result.pairingsCreated) {
                        this.missingPairingTCnts.set(result.missingPairingTCnts);
                        hardErrors.push(`Pairings are missing for team count${result.missingPairingTCnts.length > 1 ? 's' : ''}: ${result.missingPairingTCnts.join(', ')}`);
                    }
                    this.isCheckingPrereqs.set(false);
                    this.prerequisiteErrors.set(hardErrors);
                    return;
                }

                // Only pairings missing — auto-generate and proceed
                if (!result.pairingsCreated && result.missingPairingTCnts.length > 0) {
                    this.autoBuildSvc.ensurePairings({ teamCounts: result.missingPairingTCnts }).subscribe({
                        next: () => {
                            this.isCheckingPrereqs.set(false);
                            this.openAutoScheduleModal();
                        },
                        error: () => {
                            this.isCheckingPrereqs.set(false);
                            this.prerequisiteErrors.set(['Failed to auto-generate pairings']);
                        }
                    });
                    return;
                }

                this.isCheckingPrereqs.set(false);
            },
            error: () => {
                this.isCheckingPrereqs.set(false);
                this.prerequisiteErrors.set(['Failed to check prerequisites']);
            }
        });
    }

    private openAutoScheduleModal(): void {
        this.showAutoScheduleModal.set(true);
        // Fetch game dates for the day picker (non-blocking — modal shows immediately)
        this.loadModalGameDates();
    }

    private loadModalGameDates(): void {
        const s = this.scope();
        const agId = s.level === 'agegroup' ? s.agegroupId : s.level === 'division' ? s.agegroupId : undefined;
        const divId = s.level === 'division' ? s.divId : undefined;
        this.svc.getGameDates(agId, divId).subscribe({
            next: (dates) => this.modalGameDates.set(dates),
            error: () => this.modalGameDates.set([])
        });
    }

    onEventBuildConfirmed(): void {
        this.showEventBuildConfirm.set(false);
        this.onAutoScheduleBuild({ config: { action: 'build', existingGameMode: 'rebuild' } });
    }

    /** Lightweight pairing status check — populates missingPairingTCnts for stepper step ④. */
    checkPairingStatus(): void {
        this.autoBuildSvc.checkPrerequisites().subscribe({
            next: (result) => {
                this.missingPairingTCnts.set(
                    result.pairingsCreated ? [] : result.missingPairingTCnts
                );
                this.existingPairingRounds.set(
                    (result as any).existingPairingRounds ?? {}
                );
            },
            error: () => {
                this.missingPairingTCnts.set([]);
                this.existingPairingRounds.set({});
            }
        });
    }

    dismissPrerequisiteErrors(): void {
        this.prerequisiteErrors.set([]);
        this.missingPairingTCnts.set([]);
    }

    generateMissingPairings(): void {
        const tCnts = this.missingPairingTCnts();
        if (tCnts.length === 0) return;

        this.isGeneratingPairings.set(true);
        this.autoBuildSvc.ensurePairings({ teamCounts: tCnts }).subscribe({
            next: (result) => {
                this.isGeneratingPairings.set(false);
                this.missingPairingTCnts.set([]);
                const msg = result.generated.length > 0
                    ? `Generated pairings for team count${result.generated.length > 1 ? 's' : ''}: ${result.generated.join(', ')}`
                    : 'Pairings already exist';
                this.toast.show(msg, 'success');
                // Re-check pairing status to refresh stepper
                this.checkPairingStatus();
            },
            error: () => {
                this.isGeneratingPairings.set(false);
                this.toast.show('Failed to generate pairings', 'danger');
            }
        });
    }

    /** Generate pairings with configurable rounds per division size. */
    generatePairingsWithRounds(event: { teamCounts: number[]; roundsOverrides: Record<number, number>; forceRegenerate: boolean }): void {
        if (event.teamCounts.length === 0) return;

        this.isGeneratingPairings.set(true);
        this.autoBuildSvc.ensurePairings({
            teamCounts: event.teamCounts,
            roundsOverrides: event.roundsOverrides,
            forceRegenerate: event.forceRegenerate
        } as any).subscribe({
            next: (result) => {
                this.isGeneratingPairings.set(false);
                this.missingPairingTCnts.set([]);
                const parts: string[] = [];
                for (const tCnt of result.generated) {
                    const rounds = event.roundsOverrides[tCnt];
                    parts.push(rounds ? `${tCnt}-team (${rounds} rds)` : `${tCnt}-team`);
                }
                const msg = parts.length > 0
                    ? `Generated pairings: ${parts.join(', ')}`
                    : 'Pairings already exist';
                this.toast.show(msg, 'success');
                this.checkPairingStatus();
            },
            error: () => {
                this.isGeneratingPairings.set(false);
                this.toast.show('Failed to generate pairings', 'danger');
            }
        });
    }

    /** Save inline strategy from stepper — applies uniform values as event defaults via cascade. */
    saveInlineStrategy(event: { placement: number; gapPattern: number }): void {
        const gamePlacement = event.placement === 1 ? 'V' : 'H';
        const betweenRoundRows = Math.min(event.gapPattern, 2);
        const gameGuarantee = this.cascadeSvc.cascade()?.eventDefaults.gameGuarantee ?? 3;

        this.isSavingStrategy.set(true);
        this.cascadeSvc.saveEventDefaults({ gamePlacement, betweenRoundRows, gameGuarantee }).subscribe({
            next: () => {
                this.isSavingStrategy.set(false);
                this.strategyProfiles.set(this.cascadeSvc.getStrategyEntries() as DivisionStrategyEntry[]);
                this.strategySource.set('saved');
                this.toast.show('Strategy saved', 'success');
            },
            error: () => {
                this.isSavingStrategy.set(false);
                this.toast.show('Failed to save strategy', 'danger');
            }
        });
    }


    closeAutoScheduleModal(): void {
        this.showAutoScheduleModal.set(false);
    }

    /** Handle build/delete event from the auto-schedule config modal child. */
    onAutoScheduleBuild(event: AutoScheduleBuildEvent): void {
        this.showAutoScheduleModal.set(false);
        this.showEventBuildConfirm.set(false);

        if (event.config.action === 'delete-only') {
            this.executeDeleteFromModal(event.config.filterDate);
            return;
        }

        // Build flow (existing behavior)
        this.isExecuting.set(true);
        this.openOperationModal('Building Your Schedule', 'Placing games and checking constraints...', 'bi-lightning-charge-fill');
        const buildStart = Date.now();
        const request = this.buildAutoScheduleRequest(event);

        this.autoBuildSvc.execute(request).subscribe({
            next: (result) => {
                this.buildElapsedMs.set(Date.now() - buildStart);
                this.isExecuting.set(false);
                this.showOperationModal.set(false);
                this.buildResult.set(result);
                this.showBuildResults.set(true);

                // Run QA validation in background
                this.qaLoading.set(true);
                this.qaResult.set(null);
                this.qaSvc.validate().subscribe({
                    next: (qa) => {
                        this.qaResult.set(qa);
                        this.qaLoading.set(false);
                    },
                    error: () => this.qaLoading.set(false)
                });

                this.refreshAfterBulkOperation();
            },
            error: () => {
                this.isExecuting.set(false);
                this.showOperationModal.set(false);
                this.toast.show('Auto-schedule failed', 'danger', 5000);
            }
        });
    }

    /** Delete games from the config modal (supports optional day filter). */
    private executeDeleteFromModal(filterDate?: string): void {
        const s = this.scope();
        this.isDeletingGames.set(true);

        let delete$: Observable<unknown>;
        // gameDate field exists on backend DTOs — regenerate API models after backend restart
        switch (s.level) {
            case 'division':
                delete$ = this.svc.deleteDivisionGames({ divId: s.divId, gameDate: filterDate } as any);
                break;
            case 'agegroup':
                delete$ = this.svc.deleteAgegroupGames({ agegroupId: s.agegroupId, gameDate: filterDate } as any);
                break;
            case 'event':
                delete$ = this.autoBuildSvc.undo(filterDate);
                break;
        }

        const dayLabel = filterDate ? ' for selected day' : '';
        delete$.subscribe({
            next: () => {
                this.isDeletingGames.set(false);
                this.refreshAfterBulkOperation();
                this.toast.show(`Deleted ${this.scopeLabel()} games${dayLabel}`, 'success', 3000);
            },
            error: () => {
                this.isDeletingGames.set(false);
                this.toast.show('Delete failed', 'danger', 3000);
            }
        });
    }

    // ── Build results handlers ──

    private openOperationModal(title: string, subtitle: string, icon: string): void {
        this.operationTitle.set(title);
        this.operationSubtitle.set(subtitle);
        this.operationIcon.set(icon);
        this.showOperationModal.set(true);
    }

    onBuildDismissed(): void {
        this.showBuildResults.set(false);
        this.buildResult.set(null);
        this.qaResult.set(null);
    }

    onBuildReviewDetails(): void {
        this.showBuildResults.set(false);
        this.buildResult.set(null);
        // Keep qaResult — QA mode will use it
        this.setMode('qa');
    }

    private buildAutoScheduleRequest(event: AutoScheduleBuildEvent) {
        const s = this.scope();
        const allDivIds = this.agegroups().flatMap(ag => ag.divisions.map(d => d.divId));
        const mode = event.config.existingGameMode ?? 'rebuild';

        const waveAssignments = this.cascadeSvc.getWaveMap();
        const divisionOrder = this.configSvc.config()?.suggestedDivisionOrder ?? undefined;

        // Helper: derive agegroup wave as dominant division wave
        const deriveAgWave = (agId: string): number => {
            const ag = this.agegroups().find(a => a.agegroupId === agId);
            if (!ag) return 1;
            const divWaves = ag.divisions.map(d => waveAssignments[d.divId] ?? 1);
            if (divWaves.length === 0) return 1;
            const counts = new Map<number, number>();
            for (const w of divWaves) counts.set(w, (counts.get(w) ?? 0) + 1);
            let best = 1; let max = 0;
            for (const [w, c] of counts) { if (c > max) { max = c; best = w; } }
            return best;
        };

        // Build agegroup order based on scope
        let agegroupOrder: AgegroupBuildEntry[];
        let excludedDivisionIds: string[];

        switch (s.level) {
            case 'division': {
                agegroupOrder = [{ agegroupId: s.agegroupId, wave: deriveAgWave(s.agegroupId) }];
                excludedDivisionIds = allDivIds.filter(id => id !== s.divId);
                break;
            }
            case 'agegroup': {
                agegroupOrder = [{ agegroupId: s.agegroupId, wave: deriveAgWave(s.agegroupId) }];
                const agDivIds = this.agegroups()
                    .find(ag => ag.agegroupId === s.agegroupId)!
                    .divisions.map(d => d.divId);
                excludedDivisionIds = allDivIds.filter(id => !agDivIds.includes(id));
                break;
            }
            case 'event': {
                // All agegroups, ordered by suggested processing order if available
                agegroupOrder = this.agegroups().map(ag => ({
                    agegroupId: ag.agegroupId,
                    wave: deriveAgWave(ag.agegroupId),
                }));
                const suggestedOrder = this.configSvc.config()?.suggestedOrder;
                if (suggestedOrder && suggestedOrder.length > 0) {
                    const orderMap = new Map(suggestedOrder.map((id, i) => [id, i]));
                    agegroupOrder.sort((a, b) =>
                        (orderMap.get(a.agegroupId) ?? 999) - (orderMap.get(b.agegroupId) ?? 999)
                    );
                }
                excludedDivisionIds = [];
                break;
            }
        }

        return {
            agegroupOrder,
            excludedDivisionIds,
            divisionWaves: waveAssignments,
            divisionOrder,
            saveProfiles: true,
            existingGameMode: mode,
            gameGuarantee: this.gameSummary()?.gameGuarantee ?? undefined,
        };
    }


    // ── Refresh after bulk operations ──

    private refreshAfterBulkOperation(): void {
        this.refreshGameSummary();
        const s = this.scope();
        if (s.level === 'division') {
            this.loadDivisionData(s.divId, s.agegroupId);
        } else if (s.level === 'agegroup') {
            const ag = this.agegroups().find(a => a.agegroupId === s.agegroupId);
            if (ag?.divisions.length) {
                this.loadScheduleGrid(ag.divisions[0].divId, s.agegroupId);
            }
        } else {
            this.loadEventGrid();
        }
    }

    // ── Legacy auto-schedule (division-only) ──

    confirmAutoSchedule(): void {
        this.showAutoScheduleConfirm.set(true);
        this.autoScheduleResult.set(null);
    }

    cancelAutoSchedule(): void {
        this.showAutoScheduleConfirm.set(false);
    }

    autoScheduleDiv(): void {
        const div = this.selectedDivision();
        if (!div) return;

        this.isAutoScheduling.set(true);
        this.showAutoScheduleConfirm.set(false);
        this.autoScheduleResult.set(null);
        this.svc.autoScheduleDiv(div.divId).subscribe({
            next: (result) => {
                this.autoScheduleResult.set(result);
                this.isAutoScheduling.set(false);
                const agId = this.selectedAgegroupId();
                if (agId) {
                    this.loadDivisionData(div.divId, agId);
                }
                this.refreshGameSummary();
            },
            error: () => this.isAutoScheduling.set(false)
        });
    }

    dismissAutoScheduleResult(): void {
        this.autoScheduleResult.set(null);
    }

    // ── Move/Swap ──

    readonly selectedGame = signal<{ game: ScheduleGameDto; row: ScheduleGridRow; colIndex: number } | null>(null);

    selectGameForMove(game: ScheduleGameDto, row: ScheduleGridRow, colIndex: number): void {
        this.selectedPairing.set(null);
        if (this.selectedGame()?.game.gid === game.gid) {
            this.selectedGame.set(null);
        } else {
            this.selectedGame.set({ game, row, colIndex });
        }
    }

    moveOrSwapGame(targetRow: ScheduleGridRow, targetColIndex: number): void {
        const source = this.selectedGame();
        if (!source) return;

        const targetColumn = this.gridColumns()[targetColIndex];
        if (!targetColumn) return;

        const teamIds = [source.game.t1Id, source.game.t2Id].filter((id): id is string => !!id);
        const targetCell = targetRow.cells[targetColIndex];
        if (!targetCell) {
            const clash = this.findClashInRow(targetRow, teamIds, source.game.gid);
            if (clash) {
                this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                return;
            }
        }

        const movedGid = source.game.gid;
        this.svc.moveGame({
            gid: movedGid,
            targetGDate: targetRow.gDate,
            targetFieldId: targetColumn.fieldId
        }).subscribe({
            next: () => {
                this.selectedGame.set(null);
                const div = this.selectedDivision();
                const agId = this.selectedAgegroupId();
                if (div && agId) {
                    this.loadScheduleGrid(div.divId, agId, movedGid);
                }
            }
        });
    }

    // ── Grid cell click handler ──

    onGridCellClick(event: { row: ScheduleGridRow; colIndex: number; game: ScheduleGameDto | null }): void {
        if (this.selectedPairing()) {
            if (!event.game) {
                this.placeGame(event.row, event.colIndex);
            }
        } else if (this.selectedGame()) {
            if (!event.game || event.game.gid !== this.selectedGame()!.game.gid) {
                this.moveOrSwapGame(event.row, event.colIndex);
            }
        }
    }

    // ── Time-clash prevention helpers ──

    private resolvePairingTeamIds(pairing: PairingDto): string[] {
        const ids: string[] = [];
        const teams = this.divisionTeams();
        if (pairing.t1Type === 'T') {
            const t = teams.find(tm => tm.divRank === pairing.t1);
            if (t) ids.push(t.teamId);
        }
        if (pairing.t2Type === 'T') {
            const t = teams.find(tm => tm.divRank === pairing.t2);
            if (t) ids.push(t.teamId);
        }
        return ids;
    }

    private findClashInRow(row: ScheduleGridRow, teamIds: string[], excludeGid?: number): string | null {
        return findTimeClashInRow(row, teamIds, excludeGid ?? -1);
    }

    // ── Bracket enforcement ──

    private checkBracketPlacement(pairing: PairingDto): string | null {
        const isBracket = pairing.t1Type !== 'T' || pairing.t2Type !== 'T';
        if (!isBracket) return null;

        const agId = this.selectedAgegroupId();
        const ag = this.agegroups().find(a => a.agegroupId === agId);
        if (!ag) return null;

        if (ag.bChampionsByDivision) return null;

        const currentDivId = this.selectedDivision()?.divId;
        for (const row of this.gridRows()) {
            for (const cell of row.cells) {
                if (!cell) continue;
                if (cell.t1Type === 'T' && cell.t2Type === 'T') continue;
                if (cell.divId !== currentDivId) {
                    const ownerDiv = ag.divisions.find(d => d.divId === cell.divId);
                    return `Championship games for this agegroup are already being scheduled from ${ownerDiv?.divName ?? 'another pool'}. All bracket games must come from the same pool.`;
                }
            }
        }
        return null;
    }

    // ── Rapid-placement modal methods ──

    openRapidModal(): void {
        const first = this.pairings().find(p => p.bAvailable);
        if (!first) {
            this.toast.show('All pairings are already scheduled', 'info', 3000);
            return;
        }
        this.openRapidModalFor(first);
    }

    openRapidModalFor(pairing: PairingDto): void {
        this.rapidPairing.set(pairing);
        this.resetRapidSelections();
        this.showRapidModal.set(true);
        setTimeout(() => this.rapidFieldInputEl?.nativeElement.focus(), 100);
    }

    closeRapidModal(): void {
        this.showRapidModal.set(false);
        this.rapidPairing.set(null);
    }

    private resetRapidSelections(): void {
        this.rapidFieldFilter.set('');
        this.rapidTimeFilter.set('');
        this.rapidSelectedField.set(null);
        this.rapidSelectedTime.set(null);
        this.rapidFieldIndex.set(-1);
        this.rapidTimeIndex.set(-1);
        this.rapidFieldOpen.set(false);
        this.rapidTimeOpen.set(false);
    }

    onRapidFieldInput(event: Event): void {
        const val = (event.target as HTMLInputElement).value;
        this.rapidFieldFilter.set(val);
        this.rapidSelectedField.set(null);
        this.rapidSelectedTime.set(null);
        this.rapidTimeFilter.set('');
        this.rapidFieldIndex.set(0);
        this.rapidFieldOpen.set(true);
    }

    selectRapidField(field: ScheduleFieldColumn): void {
        this.rapidSelectedField.set(field);
        this.rapidFieldFilter.set(field.fName);
        this.rapidFieldOpen.set(false);
        setTimeout(() => {
            const slots = this.rapidOpenSlots();
            if (slots.length > 0) {
                this.rapidSelectedTime.set(slots[0]);
                this.rapidTimeFilter.set(slots[0].label);
            }
        }, 0);
    }

    onRapidTimeInput(event: Event): void {
        const val = (event.target as HTMLInputElement).value;
        this.rapidTimeFilter.set(val);
        this.rapidSelectedTime.set(null);
        this.rapidTimeIndex.set(0);
        this.rapidTimeOpen.set(true);
    }

    selectRapidTime(slot: { gDate: string; label: string; rowIndex: number }): void {
        this.rapidSelectedTime.set(slot);
        this.rapidTimeFilter.set(slot.label);
        this.rapidTimeOpen.set(false);
    }

    onRapidFieldKeydown(event: KeyboardEvent): void {
        const items = this.rapidFieldsFiltered();
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            this.rapidFieldOpen.set(true);
            this.rapidFieldIndex.update(i => Math.min(i + 1, items.length - 1));
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            this.rapidFieldIndex.update(i => Math.max(i - 1, 0));
        } else if (event.key === 'Enter' && this.rapidFieldOpen()) {
            event.preventDefault();
            const idx = this.rapidFieldIndex();
            if (idx >= 0 && idx < items.length) {
                this.selectRapidField(items[idx]);
            }
        } else if (event.key === 'Tab' && this.rapidFieldOpen()) {
            const idx = this.rapidFieldIndex();
            if (idx >= 0 && idx < items.length) {
                this.selectRapidField(items[idx]);
            }
        }
    }

    onRapidTimeKeydown(event: KeyboardEvent): void {
        const items = this.rapidTimesFiltered();
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            this.rapidTimeOpen.set(true);
            this.rapidTimeIndex.update(i => Math.min(i + 1, items.length - 1));
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            this.rapidTimeIndex.update(i => Math.max(i - 1, 0));
        } else if (event.key === 'Enter') {
            event.preventDefault();
            if (this.rapidTimeOpen() && this.rapidTimeIndex() >= 0) {
                const idx = this.rapidTimeIndex();
                if (idx < items.length) {
                    this.selectRapidTime(items[idx]);
                }
            } else {
                this.rapidPlaceGame();
            }
        }
    }

    rapidFieldBlur(): void {
        setTimeout(() => this.rapidFieldOpen.set(false), 150);
    }

    rapidTimeBlur(): void {
        setTimeout(() => this.rapidTimeOpen.set(false), 150);
    }

    rapidPlaceGame(): void {
        const pairing = this.rapidPairing();
        const field = this.rapidSelectedField();
        const time = this.rapidSelectedTime();
        const div = this.selectedDivision();
        const agId = this.selectedAgegroupId();
        if (!pairing || !field || !time || !div || !agId) return;

        const bracketBlock = this.checkBracketPlacement(pairing);
        if (bracketBlock) {
            this.toast.show(bracketBlock, 'danger', 5000);
            return;
        }

        const row = this.gridRows().find(r => r.gDate === time.gDate);
        if (row) {
            const teamIds = this.resolvePairingTeamIds(pairing);
            const clash = this.findClashInRow(row, teamIds);
            if (clash) {
                this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                return;
            }
        }

        this.isPlacing.set(true);
        this.svc.placeGame({
            pairingAi: pairing.ai,
            gDate: time.gDate,
            fieldId: field.fieldId,
            agegroupId: agId,
            divId: div.divId
        }).subscribe({
            next: (game) => {
                const colIdx = this.gridColumns().findIndex(c => c.fieldId === field.fieldId);
                this.gridResponse.update(grid => {
                    if (!grid) return grid;
                    const updatedRows = grid.rows.map((r: ScheduleGridRow) => {
                        if (r.gDate === time.gDate) {
                            const updatedCells = [...r.cells];
                            if (colIdx >= 0) updatedCells[colIdx] = game;
                            return { ...r, cells: updatedCells };
                        }
                        return r;
                    });
                    return { ...grid, rows: updatedRows };
                });
                this.pairings.update(list =>
                    list.map(p => p.ai === pairing.ai ? { ...p, bAvailable: false } : p)
                );
                this.isPlacing.set(false);

                const nextPairing = this.pairings().find(p => p.bAvailable);
                if (nextPairing) {
                    this.rapidPairing.set(nextPairing);
                    this.resetRapidSelections();
                    this.selectRapidField(field);
                    setTimeout(() => this.rapidFieldInputEl?.nativeElement.focus(), 100);
                } else {
                    this.toast.show('All pairings scheduled!', 'success', 3000);
                    this.closeRapidModal();
                }

                this.refreshGameSummary();
            },
            error: () => this.isPlacing.set(false)
        });
    }

    private flashAllDiv(): void {
        if (this.highlightAllDivTimer) clearTimeout(this.highlightAllDivTimer);
        this.highlightAllDiv.set(true);
        this.highlightAllDivTimer = setTimeout(() => this.highlightAllDiv.set(false), 2000);
    }

    private findFirstGameGidInGrid(grid: ScheduleGridResponse, divId: string): number | null {
        for (const row of grid.rows) {
            for (const cell of row.cells) {
                if (cell && cell.divId === divId) return cell.gid;
            }
        }
        return null;
    }

    // ── Helpers ──

    readonly formatTime = formatTime;
    readonly teamDes = teamDes;
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;
}
