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
import type { ScheduleScope } from '../shared/utils/scheduling-helpers';
import { DivisionNavigatorComponent } from '../shared/components/division-navigator/division-navigator.component';
import { ScheduleGridComponent } from '../shared/components/schedule-grid/schedule-grid.component';
import { OperationSpinnerModalComponent } from '../shared/components/operation-spinner-modal/operation-spinner-modal.component';
import { PairingsPanelComponent } from './components/pairings-panel/pairings-panel.component';
import { EventSummaryPanelComponent, type DevResetOptions } from './components/event-summary-panel/event-summary-panel.component';
import { AutoScheduleConfigModalComponent, type AutoScheduleBuildEvent, type AutoScheduleConfig, type ModalAgegroup } from './components/auto-schedule-config-modal/auto-schedule-config-modal.component';
import { CanvasConfigPanelComponent } from './components/canvas-config-panel/canvas-config-panel.component';
import { BuildResultsPanelComponent } from './components/build-results-panel/build-results-panel.component';
import { BulkDateAssignModalComponent } from './components/bulk-date-assign-modal/bulk-date-assign-modal.component';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { JobService } from '@infrastructure/services/job.service';
import type { GameSummaryResponse, DivisionStrategyEntry, AutoBuildResult, AutoBuildQaResult, AgegroupBuildEntry } from '@core/api';
import type { CalendarApplyEvent, FieldConfigApplyEvent } from './components/schedule-config/schedule-config.types';
import type { TimeConfigSaveEvent } from './components/schedule-config/time-config-section.component';
import { ScheduleConfigService } from './components/schedule-config/schedule-config.service';
import type { CanvasReadinessResponse } from '@core/api';

@Component({
    selector: 'app-schedule-division',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent, DivisionNavigatorComponent, ScheduleGridComponent, OperationSpinnerModalComponent, PairingsPanelComponent, EventSummaryPanelComponent, AutoScheduleConfigModalComponent, CanvasConfigPanelComponent, BuildResultsPanelComponent, BulkDateAssignModalComponent],
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
    readonly configSvc = inject(ScheduleConfigService);

    // ── Config service init tracking ──
    private configInitDone = false;
    private fullReadinessResponse: CanvasReadinessResponse | null = null;
    private agegroupsLoaded = false;
    private strategiesLoaded = false;

    @ViewChild('scheduleGrid') scheduleGrid?: ScheduleGridComponent;
    @ViewChild('rapidFieldInput') rapidFieldInputEl?: ElementRef<HTMLInputElement>;

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
    readonly isApplyingCalendar = signal(false);

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

    /** Per-division-name game counts within the current scope. Used by config modal for "keep" mode. */
    readonly scopeDivisionGameCounts = computed<Record<string, number>>(() => {
        const summary = this.gameSummary();
        if (!summary) return {};
        const s = this.scope();
        let divisions = summary.divisions;
        if (s.level === 'agegroup') {
            divisions = divisions.filter(d => d.agegroupId === s.agegroupId);
        } else if (s.level === 'division') {
            divisions = divisions.filter(d => d.divId === s.divId);
        }
        const map: Record<string, number> = {};
        for (const d of divisions) {
            map[d.divName] = (map[d.divName] ?? 0) + d.gameCount;
        }
        return map;
    });

    readonly scopeLabel = computed(() => {
        const s = this.scope();
        switch (s.level) {
            case 'event': return 'All';
            case 'agegroup': return this.selectedAgegroup()?.agegroupName ?? '';
            case 'division': return this.selectedDivision()?.divName ?? '';
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
    readonly autoScheduleConfig = signal<AutoScheduleConfig>(this.loadAutoScheduleConfig());
    readonly modalAgegroups = signal<ModalAgegroup[]>([]);
    readonly isExecuting = signal(false);
    readonly modalStrategies = signal<DivisionStrategyEntry[]>([]);
    readonly modalStrategySource = signal<string>('defaults');
    readonly modalStrategySourceName = signal<string>('');
    readonly modalStrategyLoading = signal(false);

    /** Division names relevant to the current scope — passed to modal for display filtering. */
    readonly modalScopeDivisionNames = computed<string[]>(() => {
        const s = this.scope();
        if (s.level === 'event') return [];
        if (s.level === 'division') {
            const div = this.agegroups()
                .find(ag => ag.agegroupId === s.agegroupId)
                ?.divisions.find(d => d.divId === s.divId);
            return div ? [div.divName] : [];
        }
        // agegroup scope: all division names in that agegroup
        const ag = this.agegroups().find(a => a.agegroupId === s.agegroupId);
        return ag ? ag.divisions.map(d => d.divName) : [];
    });

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

    // ── Time Config save state ──
    readonly isSavingTimeConfig = signal(false);

    // ── Field Config save state ──
    readonly isSavingFieldConfig = signal(false);
    readonly eventFields = signal<import('@core/api').EventFieldSummaryDto[]>([]);

    // ── Game guarantee save state ──
    readonly isSavingGuarantee = signal(false);

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
                this.eventFields.set(res.eventFields ?? []);
                this.fullReadinessResponse = res;
                this.tryInitConfig();
            },
            error: () => {
                this.canvasReadiness.set({});
                this.assignedFieldCount.set(0);
                this.priorYearDefaults.set(null);
                this.priorYearRounds.set(null);
            }
        });
    }

    loadStrategyProfiles(): void {
        this.autoBuildSvc.getStrategyProfiles().subscribe({
            next: (res) => {
                this.strategyProfiles.set(res.strategies);
                this.strategySource.set(res.source);
                this.strategiesLoaded = true;
                this.tryInitConfig();
            },
            error: () => {
                this.strategyProfiles.set([]);
                this.strategySource.set('defaults');
                this.strategiesLoaded = true;
                this.tryInitConfig();
            }
        });
    }

    /**
     * Try to initialize the ScheduleConfigService once all three
     * parallel loads (readiness, agegroups, strategies) are done.
     * Only runs once — subsequent data refreshes don't re-derive config.
     */
    private tryInitConfig(): void {
        if (this.configInitDone) return;
        if (!this.fullReadinessResponse || !this.agegroupsLoaded || !this.strategiesLoaded) return;

        this.configInitDone = true;
        this.configSvc.initialize(
            this.fullReadinessResponse,
            this.agegroups().map(ag => ({
                agegroupId: ag.agegroupId,
                agegroupName: ag.agegroupName,
                teamCount: agTeamCount(ag)
            })),
            this.strategyProfiles(),
            this.strategySource()
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

    /**
     * Handle calendar section "Save & Apply" — calls bulkAssignDate per date sequentially.
     * GSI/StartTime/MaxGames are read from the CURRENT effective values (readiness or defaults),
     * NOT from the calendar event (Calendar section no longer owns these).
     */
    onCalendarApply(event: CalendarApplyEvent): void {
        const dateKeys = Object.keys(event.assignments).sort();
        if (dateKeys.length === 0) return;

        this.isApplyingCalendar.set(true);

        // Derive current effective GSI/Start/Max from readiness or defaults
        const { startTime, gsi, maxGamesPerField } = this.resolveEffectiveFieldDefaults();

        let completed = 0;

        const applyNext = (): void => {
            if (completed >= dateKeys.length) {
                this.isApplyingCalendar.set(false);

                // Persist wave assignments
                this.configSvc.updateValue('waveAssignments', event.waveMap);

                // Persist representative R/day (first date's value per AG)
                const rpdMap: Record<string, number> = {};
                for (const dk of dateKeys) {
                    for (const entry of event.assignments[dk].entries) {
                        rpdMap[entry.agegroupId] ??= entry.roundsPerDay ?? 1;
                    }
                }
                this.configSvc.updateValue('roundsPerDay', rpdMap);

                this.loadCanvasReadiness();
                this.configSvc.saveToLocalStorage();
                this.toast.show(`Applied ${dateKeys.length} date(s) successfully`, 'success');
                return;
            }

            const isoDate = dateKeys[completed];
            const assignment = event.assignments[isoDate];
            const gDate = new Date(isoDate + 'T00:00:00');

            this.timeslotSvc.bulkAssignDate({
                gDate: gDate.toISOString(),
                startTime,
                gamestartInterval: gsi,
                maxGamesPerField,
                entries: assignment.entries,
                removedAgegroupIds: assignment.removedAgegroupIds
            }).subscribe({
                next: () => {
                    completed++;
                    applyNext();
                },
                error: () => {
                    this.isApplyingCalendar.set(false);
                    this.toast.show(`Failed on date ${isoDate}`, 'danger');
                    if (completed > 0) this.loadCanvasReadiness();
                }
            });
        };

        applyNext();
    }

    /**
     * Time Config save: uses updateFieldConfig to update GSI/StartTime/MaxGames
     * on existing field timeslot rows. Sends per-AG-per-DOW overrides from the matrix.
     */
    onTimeConfigSave(event: TimeConfigSaveEvent): void {
        this.isSavingTimeConfig.set(true);

        // When per-AG-per-DOW overrides are present, send GSI via per-AG entries
        // and let overrides handle StartTime + MaxGamesPerField.
        // Do NOT send baseStartTime/maxGamesPerField as uniform values — that would
        // trigger ApplyUniformConfig which recalculates ALL wave offsets destructively.
        const hasOverrides = (event.agDowOverrides?.length ?? 0) > 0;

        // Always send GSI as per-AG entries so each agegroup gets its value
        // without triggering the uniform config path for StartTime/MaxGames
        let entries: Array<{ agegroupId: string; gamestartInterval?: number }> | undefined;
        if (hasOverrides) {
            // Send GSI for every agegroup as per-AG entries (not uniform)
            const rows = typeof event.gsi === 'object' ? event.gsi : {};
            const uniformGsi = typeof event.gsi === 'number' ? event.gsi : undefined;
            const ags = this.agegroups();
            entries = ags.map(ag => ({
                agegroupId: ag.agegroupId,
                gamestartInterval: rows[ag.agegroupId] ?? uniformGsi
            }));
        } else if (event.gsiScope === 'per-ag' && typeof event.gsi === 'object') {
            entries = Object.entries(event.gsi).map(([agId, gsi]) => ({
                agegroupId: agId,
                gamestartInterval: gsi
            }));
        }

        this.timeslotSvc.updateFieldConfig({
            // Only send uniform start/max when there are NO per-DOW overrides
            baseStartTime: hasOverrides ? undefined : event.startTime,
            gamestartInterval: hasOverrides ? undefined : (typeof event.gsi === 'number' ? event.gsi : undefined),
            maxGamesPerField: hasOverrides ? undefined : event.maxGamesPerField,
            entries,
            agDowOverrides: hasOverrides ? event.agDowOverrides : undefined
        }).subscribe({
            next: (result) => {
                this.isSavingTimeConfig.set(false);
                this.loadCanvasReadiness();
                this.configSvc.saveToLocalStorage();
                this.toast.show(`Time config updated (${result.rowsUpdated} rows)`, 'success');
            },
            error: () => {
                this.isSavingTimeConfig.set(false);
                this.toast.show('Failed to update time config', 'danger');
            }
        });
    }

    onFieldConfigApply(event: FieldConfigApplyEvent): void {
        this.isSavingFieldConfig.set(true);

        // Build entries: for AGs with overrides, send their field list.
        // For AGs NOT in overrides, send all event fields (= full set, no restriction).
        const allFieldIds = this.eventFields().map(f => f.fieldId);
        const entries = this.agegroups().map(ag => ({
            agegroupId: ag.agegroupId,
            fieldIds: event.overrides[ag.agegroupId] ?? allFieldIds
        }));

        this.timeslotSvc.saveFieldAssignments({ entries }).subscribe({
            next: (result) => {
                this.isSavingFieldConfig.set(false);
                this.loadCanvasReadiness();
                const msg = result.rowsCreated + result.rowsDeleted > 0
                    ? `Field assignments updated (+${result.rowsCreated} / -${result.rowsDeleted} rows)`
                    : 'Field assignments saved (no changes)';
                this.toast.show(msg, 'success');
            },
            error: () => {
                this.isSavingFieldConfig.set(false);
                this.toast.show('Failed to save field assignments', 'danger');
            }
        });
    }

    saveGameGuarantee(event: { eventDefault: number | null }): void {
        this.isSavingGuarantee.set(true);
        this.autoBuildSvc.saveGameGuarantee({ eventDefault: event.eventDefault ?? undefined }).subscribe({
            next: () => {
                this.isSavingGuarantee.set(false);
                this.loadCanvasReadiness();
                this.refreshGameSummary();
                this.toast.show('Game guarantee saved', 'success');
            },
            error: () => {
                this.isSavingGuarantee.set(false);
                this.toast.show('Failed to save game guarantee', 'danger');
            }
        });
    }

    /**
     * Resolve current effective GSI/StartTime/MaxGames from readiness data or defaults.
     * Used by onCalendarApply to create new field timeslots with the right values.
     */
    private resolveEffectiveFieldDefaults(): { startTime: string; gsi: number; maxGamesPerField: number } {
        const map = this.canvasReadiness();
        const configured = Object.values(map).find(a => a.isConfigured && a.gamestartInterval != null);
        if (configured) {
            return {
                startTime: configured.startTime ?? '8:00 AM',
                gsi: configured.gamestartInterval ?? 60,
                maxGamesPerField: configured.maxGamesPerField ?? 8
            };
        }
        const py = this.priorYearDefaults();
        if (py) {
            return {
                startTime: py.startTime ?? '8:00 AM',
                gsi: py.gamestartInterval ?? 60,
                maxGamesPerField: py.maxGamesPerField ?? 8
            };
        }
        return { startTime: '8:00 AM', gsi: 60, maxGamesPerField: 8 };
    }

    closeBulkDateModal(): void {
        this.showBulkDateModal.set(false);
    }

    // ── Scope selection handlers (wired from navigator outputs) ──

    onEventSelected(): void {
        this.scope.set({ level: 'event' });
        this.dismissBuildResults();
        this.clearDivisionState();
    }

    navigateToManageFields(): void {
        this.router.navigate(['../fields'], { relativeTo: this.route });
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

    loadScheduleGrid(divId: string, agegroupId: string): void {
        if (!this.gridResponse()) {
            this.isGridLoading.set(true);
        }
        this.svc.getScheduleGrid(divId, agegroupId).subscribe({
            next: (grid) => {
                this.gridResponse.set(grid);
                this.isGridLoading.set(false);
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
        const clash = this.findTimeClashInRow(row, teamIds);
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
        const parts: string[] = [];
        if (options.games) parts.push('games');
        if (options.strategyProfiles) parts.push('strategy profiles');
        if (options.pairings) parts.push('pairings');
        if (options.dates) parts.push('dates');
        if (options.fieldTimeslots) parts.push('field timeslots');
        const hasSource = !!options.sourceJobId;
        const modalMsg = hasSource
            ? `Clearing ${parts.join(', ')}… then preconfiguring from source`
            : `Clearing ${parts.join(', ')}…`;
        this.openOperationModal('Resetting Scheduling Data', modalMsg, 'bi-arrow-counterclockwise');
        this.autoBuildSvc.resetSchedule({
            games: options.games,
            strategyProfiles: options.strategyProfiles,
            pairings: options.pairings,
            dates: options.dates,
            fieldTimeslots: options.fieldTimeslots,
            fieldAssignments: false,
            sourceJobId: options.sourceJobId
        }).subscribe({
            next: (result) => {
                this.showOperationModal.set(false);
                this.isResetting.set(false);
                const summary: string[] = [];
                if (result.gamesDeleted) summary.push(`${result.gamesDeleted} games`);
                if (result.agegroupsCleared) summary.push(`${result.agegroupsCleared} agegroups`);
                if (result.pairingGroupsCleared) summary.push(`${result.pairingGroupsCleared} pairing groups`);
                if (result.preconfig) {
                    const p = result.preconfig;
                    const seeded: string[] = [];
                    if (p.colorsApplied) seeded.push(`${p.colorsApplied} colors`);
                    if (p.datesSeeded) seeded.push(`${p.datesSeeded} ag dates`);
                    if (p.fieldAssignmentsSeeded) seeded.push(`${p.fieldAssignmentsSeeded} ag fields`);
                    if (p.pairingsGenerated?.length) seeded.push(`pairings for ${p.pairingsGenerated.join(', ')}-team`);
                    if (seeded.length) summary.push(`seeded: ${seeded.join(', ')}`);
                }
                this.toast.show(
                    `Reset complete: ${summary.length > 0 ? summary.join(', ') + ' cleared' : 'nothing to clear'}`,
                    'success', 8000
                );
                this.configSvc.clearLocalStorage();
                this.configSvc.reset();
                this.configInitDone = false;
                this.strategiesLoaded = false;

                this.refreshAfterBulkOperation();
                this.loadCanvasReadiness();
                this.loadStrategyProfiles();
                this.checkPairingStatus();
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
                this.isCheckingPrereqs.set(false);
                if (!result.allPassed) {
                    const errors: string[] = [];
                    if (!result.poolsAssigned) {
                        errors.push(`${result.unassignedTeamCount} team${result.unassignedTeamCount > 1 ? 's' : ''} haven't been assigned to a pool yet`);
                    }
                    if (!result.pairingsCreated) {
                        this.missingPairingTCnts.set(result.missingPairingTCnts);
                        errors.push(`Pairings are missing for team count${result.missingPairingTCnts.length > 1 ? 's' : ''}: ${result.missingPairingTCnts.join(', ')}`);
                    }
                    if (!result.timeslotsConfigured) {
                        errors.push(`Timeslots not configured for: ${result.agegroupsMissingTimeslots.join(', ')}`);
                    }
                    this.prerequisiteErrors.set(errors);
                    return;
                }
                // All prerequisites passed — open the config modal
                this.openAutoScheduleModal();
            },
            error: () => {
                this.isCheckingPrereqs.set(false);
                this.prerequisiteErrors.set(['Failed to check prerequisites']);
            }
        });
    }

    private openAutoScheduleModal(): void {
        const config = this.loadAutoScheduleConfig();
        // Default to "keep" when games exist in scope — protect hand-tweaked work
        if (this.hasGamesInScope()) {
            config.existingGameMode = 'keep';
        }
        this.autoScheduleConfig.set(config);
        // Load strategy profiles from backend (three-layer resolution)
        this.modalStrategyLoading.set(true);
        this.autoBuildSvc.getStrategyProfiles().subscribe({
            next: (response) => {
                this.modalStrategies.set(response.strategies.map(s => ({ ...s })));
                this.modalStrategySource.set(response.source);
                this.modalStrategySourceName.set(response.inferredFromJobName ?? '');
                this.modalStrategyLoading.set(false);
            },
            error: () => {
                this.modalStrategies.set([]);
                this.modalStrategyLoading.set(false);
            }
        });
        // Populate modal agegroup list (only relevant at event scope)
        if (this.scope().level === 'event') {
            const waveAssignments = this.configSvc.config()?.waveAssignments ?? {};
            this.modalAgegroups.set(this.agegroups().map(ag => ({
                agegroupId: ag.agegroupId,
                agegroupName: ag.agegroupName,
                color: ag.color ?? null,
                teamCount: agTeamCount(ag),
                divisionCount: ag.divisions.length,
                included: true,
                wave: waveAssignments[ag.agegroupId] ?? 1,
            })));
        }
        this.showAutoScheduleModal.set(true);
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

    /** Save inline strategy from stepper — applies uniform values to ALL divisions. */
    saveInlineStrategy(event: { placement: number; gapPattern: number }): void {
        let entries = this.strategyProfiles();

        // When on defaults (no saved profiles), build entries from unique division names
        if (entries.length === 0) {
            const divNames = new Set<string>();
            for (const ag of this.agegroups()) {
                for (const div of ag.divisions) {
                    divNames.add(div.divName);
                }
            }
            if (divNames.size === 0) return;
            entries = [...divNames].map(name => ({
                divisionName: name,
                placement: event.placement,
                gapPattern: event.gapPattern
            }));
        }

        const updated = entries.map(s => ({
            ...s,
            placement: event.placement,
            gapPattern: event.gapPattern
        }));

        this.isSavingStrategy.set(true);
        this.autoBuildSvc.saveStrategyProfiles(updated).subscribe({
            next: (res) => {
                this.isSavingStrategy.set(false);
                this.strategyProfiles.set(res.strategies);
                this.strategySource.set(res.source);
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

    /** Handle build event from the auto-schedule config modal child. */
    onAutoScheduleBuild(event: AutoScheduleBuildEvent): void {
        // Persist the config choice to localStorage
        this.saveAutoScheduleConfig(event.config);
        this.autoScheduleConfig.set(event.config);

        this.isExecuting.set(true);
        this.showAutoScheduleModal.set(false);
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

    onBuildRunAgain(): void {
        this.showBuildResults.set(false);
        this.buildResult.set(null);
        this.qaResult.set(null);
        this.openAutoScheduleConfig();
    }

    onBuildUndo(): void {
        this.isUndoing.set(true);
        this.openOperationModal('Removing Games', 'Undoing all placed games...', 'bi-arrow-counterclockwise');
        this.autoBuildSvc.undo().subscribe({
            next: (result) => {
                this.isUndoing.set(false);
                this.showOperationModal.set(false);
                this.showBuildResults.set(false);
                this.buildResult.set(null);
                this.qaResult.set(null);
                this.refreshAfterBulkOperation();
                this.toast.show(`Removed ${result.gamesDeleted} games`, 'success', 3000);
            },
            error: () => {
                this.isUndoing.set(false);
                this.showOperationModal.set(false);
                this.toast.show('Undo failed', 'danger', 5000);
            }
        });
    }

    private buildAutoScheduleRequest(event: AutoScheduleBuildEvent) {
        const s = this.scope();
        const allDivIds = this.agegroups().flatMap(ag => ag.divisions.map(d => d.divId));
        const mode = event.config.existingGameMode ?? 'rebuild';

        const waveAssignments = this.configSvc.config()?.waveAssignments ?? {};

        switch (s.level) {
            case 'division': {
                const excluded = allDivIds.filter(id => id !== s.divId);
                return {
                    agegroupOrder: [{ agegroupId: s.agegroupId, wave: waveAssignments[s.agegroupId] ?? 1 }] satisfies AgegroupBuildEntry[],
                    divisionOrderStrategy: event.config.divisionOrderStrategy,
                    excludedDivisionIds: excluded,
                    divisionStrategies: event.strategies,
                    saveProfiles: true,
                    existingGameMode: mode,
                    gameGuarantee: this.gameSummary()?.gameGuarantee ?? undefined,
                };
            }
            case 'agegroup': {
                const agDivIds = this.agegroups()
                    .find(ag => ag.agegroupId === s.agegroupId)!
                    .divisions.map(d => d.divId);
                const excluded = allDivIds.filter(id => !agDivIds.includes(id));
                return {
                    agegroupOrder: [{ agegroupId: s.agegroupId, wave: waveAssignments[s.agegroupId] ?? 1 }] satisfies AgegroupBuildEntry[],
                    divisionOrderStrategy: event.config.divisionOrderStrategy,
                    excludedDivisionIds: excluded,
                    divisionStrategies: event.strategies,
                    saveProfiles: true,
                    existingGameMode: mode,
                    gameGuarantee: this.gameSummary()?.gameGuarantee ?? undefined,
                };
            }
            case 'event': {
                const included = event.agegroups.filter(ag => ag.included);
                const includedDivIds = new Set(
                    this.agegroups()
                        .filter(ag => included.some(m => m.agegroupId === ag.agegroupId))
                        .flatMap(ag => ag.divisions.map(d => d.divId))
                );
                const excluded = allDivIds.filter(id => !includedDivIds.has(id));
                return {
                    agegroupOrder: included.map(ag => ({ agegroupId: ag.agegroupId, wave: ag.wave })) satisfies AgegroupBuildEntry[],
                    divisionOrderStrategy: event.config.divisionOrderStrategy,
                    excludedDivisionIds: excluded,
                    divisionStrategies: event.strategies,
                    saveProfiles: true,
                    existingGameMode: mode,
                    gameGuarantee: this.gameSummary()?.gameGuarantee ?? undefined,
                };
            }
        }
    }

    private loadAutoScheduleConfig(): AutoScheduleConfig {
        try {
            const raw = localStorage.getItem(LocalStorageKey.AutoScheduleConfig);
            if (raw) {
                const parsed = JSON.parse(raw);
                return {
                    divisionOrderStrategy: parsed.divisionOrderStrategy === 'odd-first' ? 'odd-first' : 'alpha',
                    existingGameMode: parsed.existingGameMode === 'keep' ? 'keep' : 'rebuild',
                };
            }
        } catch { /* ignore parse errors */ }
        return { divisionOrderStrategy: 'alpha', existingGameMode: 'rebuild' };
    }

    private saveAutoScheduleConfig(config: AutoScheduleConfig): void {
        localStorage.setItem(LocalStorageKey.AutoScheduleConfig, JSON.stringify(config));
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
            this.gridResponse.set(null);
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
            const clash = this.findTimeClashInRow(targetRow, teamIds, source.game.gid);
            if (clash) {
                this.toast.show(`Time clash: ${clash} is already playing at this timeslot`, 'danger', 4000);
                return;
            }
        }

        this.svc.moveGame({
            gid: source.game.gid,
            targetGDate: targetRow.gDate,
            targetFieldId: targetColumn.fieldId
        }).subscribe({
            next: () => {
                this.selectedGame.set(null);
                const div = this.selectedDivision();
                const agId = this.selectedAgegroupId();
                if (div && agId) this.loadScheduleGrid(div.divId, agId);
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

    private findTimeClashInRow(row: ScheduleGridRow, teamIds: string[], excludeGid?: number): string | null {
        for (const cell of row.cells) {
            if (!cell) continue;
            if (excludeGid != null && cell.gid === excludeGid) continue;
            for (const tid of teamIds) {
                if (cell.t1Id === tid || cell.t2Id === tid) {
                    const team = this.divisionTeams().find(t => t.teamId === tid);
                    return team?.teamName ?? 'A team';
                }
            }
        }
        return null;
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
            const clash = this.findTimeClashInRow(row, teamIds);
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
