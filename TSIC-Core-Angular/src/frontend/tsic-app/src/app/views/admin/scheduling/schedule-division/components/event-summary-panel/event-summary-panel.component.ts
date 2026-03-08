import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';


import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, DivisionStrategyEntry, EventFieldSummaryDto, GameDayDto, GameSummaryResponse, PriorYearFieldDefaults } from '@core/api';
import { CalendarSectionComponent } from '../schedule-config/calendar-section.component';
import { TimeConfigSectionComponent, type TimeConfigSaveEvent, type DateColumnInfo } from '../schedule-config/time-config-section.component';
import { BuildSectionComponent } from '../schedule-config/build-section.component';
import { FieldConfigSectionComponent } from '../schedule-config/field-config-section.component';
import { PairingsSectionComponent, type PairingsGenerateEvent } from '../schedule-config/pairings-section.component';
import type { CalendarApplyEvent, FieldConfigApplyEvent, StepperSection } from '../schedule-config/schedule-config.types';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

export type AgStage = 'unconfigured' | 'configured' | 'scheduled-partial' | 'scheduled-complete';

export interface DevResetOptions {
    games: boolean;
    strategyProfiles: boolean;
    pairings: boolean;
    /** Clear date/round assignments (TimeslotsLeagueSeasonDates) */
    dates: boolean;
    /** Clear field-timeslot configuration (TimeslotsLeagueSeasonFields — start time, GSI, max games) */
    fieldTimeslots: boolean;
    /** When set, preconfigure (colors, dates, fields, pairings) from this source job after reset. */
    sourceJobId?: string;
}

/** Formatted game day line for hero display. */
export interface GameDayLine {
    dayNumber: number;
    dow: string;
    dateFormatted: string;
    fieldCount: number;
    startTime: string;
    endTime: string;
    gsi: number;
    totalSlots: number;
}


@Component({
    selector: 'app-event-summary-panel',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, CalendarSectionComponent, TimeConfigSectionComponent, BuildSectionComponent, FieldConfigSectionComponent, PairingsSectionComponent, TsicDialogComponent],
    templateUrl: './event-summary-panel.component.html',
    styleUrl: './event-summary-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventSummaryPanelComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly gameSummary = input<GameSummaryResponse | null>(null);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly strategies = input<DivisionStrategyEntry[]>([]);
    readonly strategySource = input('defaults');
    readonly assignedFieldCount = input(0);
    readonly hasGamesInScope = input(false);
    readonly scopeGameCount = input(0);
    readonly isExecuting = input(false);
    readonly isDeletingGames = input(false);
    readonly showDeleteConfirm = input(false);
    readonly isResetting = input(false);
    readonly missingPairingTCnts = input<number[]>([]);
    readonly existingPairingRounds = input<Record<number, number>>({});
    readonly isGeneratingPairings = input(false);
    readonly isSavingStrategy = input(false);
    readonly isSavingTimeConfig = input(false);
    readonly isSavingFieldConfig = input(false);
    readonly isApplyingCalendar = input(false);
    readonly eventFields = input<EventFieldSummaryDto[]>([]);
    readonly priorYearDefaults = input<PriorYearFieldDefaults | null>(null);
    readonly priorYearRounds = input<Record<string, number> | null>(null);
    readonly eventTypeLabel = input<string>('Tournament');
    readonly configScenario = input<'saved' | 'prior-year' | 'new'>('new');
    readonly configPriorYearLabel = input<string | null>(null);
    readonly configWaves = input<Record<string, number>>({});
    readonly configRoundsPerDay = input<Record<string, number>>({});

    // ── Outputs ──
    readonly autoScheduleRequested = output<void>();
    readonly deleteRequested = output<void>();
    readonly deleteConfirmed = output<void>();
    readonly deleteCancelled = output<void>();
    readonly agegroupClicked = output<string>();
    readonly manageFieldsClicked = output<void>();
    readonly generatePairingsRequested = output<void>();
    readonly generatePairingsWithRoundsRequested = output<PairingsGenerateEvent>();
    readonly bulkDateAssignRequested = output<void>();
    readonly saveStrategyRequested = output<{ placement: number; gapPattern: number }>();
    readonly resetConfirmed = output<DevResetOptions>();
    readonly calendarApplyRequested = output<CalendarApplyEvent>();
    readonly timeConfigSaveRequested = output<TimeConfigSaveEvent>();
    readonly fieldConfigApplyRequested = output<FieldConfigApplyEvent>();

    // ── Local state ──
    readonly deleteConfirmText = signal('');
    readonly showResetConfirm = signal(false);
    readonly resetConfirmText = signal('');

    // ── Dev reset checklist ──
    readonly resetGames = signal(true);
    readonly resetStrategyProfiles = signal(true);
    readonly resetPairings = signal(true);
    readonly resetDates = signal(false);
    readonly resetFieldTimeslots = signal(false);

    readonly anyResetChecked = computed(() =>
        this.resetGames() || this.resetStrategyProfiles() || this.resetPairings() ||
        this.resetDates() || this.resetFieldTimeslots()
    );

    // ── Accordion: which section is expanded (null = all collapsed) ──
    readonly expandedSection = signal<StepperSection | null>(null);

    // ── Calendar modal ──
    readonly showCalendarModal = signal(false);

    // ── Stepper: local strategy overrides (null = user hasn't touched it) ──
    readonly localPlacement = signal<number | null>(null);
    readonly localGapPattern = signal<number | null>(null);

    // ── Helpers ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    // ── Event-level computed ──

    readonly totalExpected = computed(() => {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions.reduce((sum, d) => sum + d.expectedRrGames, 0);
    });

    readonly totalGames = computed(() => this.gameSummary()?.totalGames ?? 0);

    readonly gameGuarantee = computed(() => this.gameSummary()?.gameGuarantee ?? null);

    readonly completionPct = computed(() => {
        const expected = this.totalExpected();
        if (expected === 0) return 0;
        return Math.round((this.totalGames() / expected) * 100);
    });

    readonly configuredCount = computed(() =>
        Object.values(this.readinessMap()).filter(r => r.isConfigured).length
    );

    readonly totalAgegroups = computed(() => this.agegroups().length);

    /** True when there's nothing to clear AND no prior year to preconfigure from. */
    readonly isResetEmpty = computed(() =>
        this.configuredCount() === 0 && this.totalGames() === 0
        && !this.priorYearDefaults()?.priorJobId
    );

    // ── Stepper step completion ──

    /** Step ①: At least one agegroup configured (dates+fields) */
    readonly datesComplete = computed(() =>
        this.totalAgegroups() > 0 && this.configuredCount() === this.totalAgegroups()
    );

    /** Step ②: Fields assigned */
    readonly fieldsComplete = computed(() => this.assignedFieldCount() > 0);

    /** Step ③: Pairings generated for all team counts */
    readonly pairingsComplete = computed(() =>
        this.missingPairingTCnts().length === 0
    );

    /** Step ④: Strategy always has an effective value (defaults or saved) */
    readonly strategyComplete = computed(() => true);

    // ── Time Config summary computed (current effective values from readiness or defaults) ──

    readonly effectiveGsi = computed((): number => {
        const configured = Object.values(this.readinessMap())
            .find(a => a.isConfigured && a.gamestartInterval != null);
        if (configured) return configured.gamestartInterval!;
        return this.priorYearDefaults()?.gamestartInterval ?? 60;
    });

    readonly effectiveStartTime = computed((): string => {
        const configured = Object.values(this.readinessMap())
            .find(a => a.isConfigured && a.startTime);
        if (configured) return configured.startTime!;
        return this.priorYearDefaults()?.startTime ?? '8:00 AM';
    });

    readonly effectiveMaxGames = computed((): number => {
        const configured = Object.values(this.readinessMap())
            .find(a => a.isConfigured && a.maxGamesPerField != null);
        if (configured) return configured.maxGamesPerField!;
        return this.priorYearDefaults()?.maxGamesPerField ?? 8;
    });

    // ── Accordion toggle ──

    toggleSection(section: StepperSection): void {
        this.expandedSection.set(
            this.expandedSection() === section ? null : section
        );
    }

    openCalendarModal(): void {
        this.expandedSection.set(null); // collapse any expanded inline section
        this.showCalendarModal.set(true);
    }

    closeCalendarModal(): void {
        this.showCalendarModal.set(false);
    }

    onCalendarApply(event: CalendarApplyEvent): void {
        this.calendarApplyRequested.emit(event);
    }

    onTimeConfigSave(event: TimeConfigSaveEvent): void {
        this.timeConfigSaveRequested.emit(event);
    }

    onFieldConfigApply(event: FieldConfigApplyEvent): void {
        this.fieldConfigApplyRequested.emit(event);
    }

    onPairingsGenerate(event: PairingsGenerateEvent): void {
        this.generatePairingsWithRoundsRequested.emit(event);
    }

    /** Uniform placement value across all strategies, or null if mixed */
    readonly uniformPlacement = computed((): number | null => {
        const strats = this.strategies();
        if (strats.length === 0) return null;
        const val = this.uniformValue(strats, s => s.placement.toString());
        return val !== null ? Number(val) : null;
    });

    /** Uniform gap pattern value across all strategies, or null if mixed */
    readonly uniformGap = computed((): number | null => {
        const strats = this.strategies();
        if (strats.length === 0) return null;
        const val = this.uniformValue(strats, s => s.gapPattern.toString());
        return val !== null ? Number(val) : null;
    });

    /** Effective placement: local override → server value → default */
    readonly effectivePlacement = computed(() =>
        this.localPlacement() ?? this.uniformPlacement() ?? 0
    );

    /** Effective gap pattern: local override → server value → default */
    readonly effectiveGapPattern = computed(() =>
        this.localGapPattern() ?? this.uniformGap() ?? 1
    );

    /** True when effective strategy differs from server/default values */
    readonly strategyDirty = computed(() => {
        // No override = user hasn't touched anything
        if (this.localPlacement() === null && this.localGapPattern() === null) return false;
        const sp = this.uniformPlacement() ?? 0;
        const sg = this.uniformGap() ?? 1;
        return this.effectivePlacement() !== sp || this.effectiveGapPattern() !== sg;
    });

    // ── Tournament-level game days (union of ALL dates across agegroups) ──

    readonly eventGameDays = computed((): GameDayLine[] => {
        const map = this.readinessMap();
        const configured = Object.values(map).filter(r => r.isConfigured);
        if (configured.length === 0) return [];

        // Aggregate ALL unique dates across ALL configured agegroups
        const dateMap = new Map<string, GameDayDto>(); // ISO date key → GameDayDto
        for (const r of configured) {
            if (!r.gameDays) continue;
            for (const gd of r.gameDays) {
                const dateKey = gd.date.substring(0, 10); // YYYY-MM-DD
                if (!dateMap.has(dateKey)) {
                    dateMap.set(dateKey, gd);
                }
            }
        }

        // Sort by date and renumber as tournament days
        const sorted = [...dateMap.values()].sort((a, b) =>
            new Date(a.date).getTime() - new Date(b.date).getTime()
        );

        return sorted.map((gd, i) => ({
            ...this.toGameDayLine(gd),
            dayNumber: i + 1
        }));
    });

    /** All unique dates across configured agegroups, formatted for the time config matrix. */
    readonly allDatesForMatrix = computed((): DateColumnInfo[] => {
        const map = this.readinessMap();
        const configured = Object.values(map).filter(r => r.isConfigured);
        const dateMap = new Map<string, { dow: string }>();
        for (const r of configured) {
            for (const gd of r.gameDays ?? []) {
                const key = gd.date.substring(0, 10);
                if (!dateMap.has(key)) {
                    dateMap.set(key, { dow: gd.dow });
                }
            }
        }
        return [...dateMap.entries()]
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([iso, info]) => {
                const d = new Date(iso + 'T00:00:00');
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                return { isoDate: iso, dow: info.dow, dateFormatted: `${mm}/${dd}/${d.getFullYear()}` };
            });
    });

    /**
     * If ALL configured agegroups play the same dates, return a hero-level "Plays:" string.
     * Otherwise null — per-tile dropdowns handle it.
     */
    readonly heroPlaysLabel = computed((): string | null => {
        const map = this.readinessMap();
        const configured = Object.values(map).filter(r => r.isConfigured && r.gameDays?.length > 0);
        if (configured.length === 0) return null;

        // Build a canonical date signature for each agegroup
        const sigs = configured.map(r =>
            [...r.gameDays].sort((a, b) => a.date.localeCompare(b.date))
                .map(gd => gd.date.substring(0, 10))
                .join(',')
        );

        // Check if all are identical
        if (!sigs.every(s => s === sigs[0])) return null;

        // All the same — format using the first agegroup's data
        const gameDayList = this.agGameDayList(configured[0].agegroupId);
        if (gameDayList.length === 0) return null;
        return `Plays: ${gameDayList.join(' & ')}`;
    });

    /** Strategy status label for step ④ */
    readonly strategyStatusLabel = computed((): string => {
        const strats = this.strategies();
        if (strats.length === 0 || this.strategySource() === 'defaults') return 'Using defaults';
        const p = this.uniformPlacement();
        const g = this.uniformGap();
        const parts: string[] = [];
        if (p !== null) parts.push(this.placementLabel(p));
        if (g !== null) parts.push(this.gapLabel(g));
        return parts.length > 0 ? parts.join(' · ') : 'Mixed per-division';
    });

    /** Pairings status label for step ③ */
    readonly pairingsStatusLabel = computed((): string => {
        const missing = this.missingPairingTCnts();
        if (missing.length === 0) return 'All generated';
        return `Missing for ${missing.join(', ')} teams`;
    });

    onSaveStrategy(): void {
        this.saveStrategyRequested.emit({
            placement: this.effectivePlacement(),
            gapPattern: this.effectiveGapPattern()
        });
    }

    onStrategyCancelled(): void {
        this.localPlacement.set(null);
        this.localGapPattern.set(null);
        this.toggleSection('strategy');
    }

    // ── Per-agegroup helpers ──

    agGameCount(agegroupId: string): number {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions
            .filter(d => d.agegroupId === agegroupId)
            .reduce((sum, d) => sum + d.gameCount, 0);
    }

    agExpectedGames(agegroupId: string): number {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions
            .filter(d => d.agegroupId === agegroupId)
            .reduce((sum, d) => sum + d.expectedRrGames, 0);
    }

    agStage(agegroupId: string): AgStage {
        const r = this.readinessMap()[agegroupId];
        if (!r?.isConfigured) return 'unconfigured';
        const games = this.agGameCount(agegroupId);
        if (games === 0) return 'configured';
        const expected = this.agExpectedGames(agegroupId);
        return games >= expected ? 'scheduled-complete' : 'scheduled-partial';
    }

    /**
     * Per-card game day list — returns formatted date strings for the plays dropdown.
     * e.g. ["Sat 06/06/2026", "Sun 06/07/2026"]
     * Returns empty array if unconfigured.
     */
    agGameDayList(agegroupId: string): string[] {
        const r = this.readinessMap()[agegroupId];
        if (!r?.isConfigured || !r.gameDays || r.gameDays.length === 0) return [];

        return [...r.gameDays]
            .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
            .map(gd => {
                const d = new Date(gd.date);
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                const yyyy = d.getFullYear();
                return `${gd.dow} ${mm}/${dd}/${yyyy}`;
            });
    }

    /**
     * Per-tile game day list — returns empty if hero already shows a uniform "Plays:" line.
     * This avoids redundant dropdowns when all agegroups play the same dates.
     */
    agTileGameDayList(agegroupId: string): string[] {
        if (this.heroPlaysLabel()) return []; // hero covers it
        return this.agGameDayList(agegroupId);
    }

    // ── Labels ──

    placementLabel(val: number): string {
        return val === 1 ? 'Fill fields first' : 'Spread across time slots';
    }

    gapLabel(val: number): string {
        switch (val) {
            case 0: return 'No rest';
            case 2: return '2 game break';
            default: return '1 game break';
        }
    }

    // ── Private helpers ──

    private uniformValue<T>(items: T[], extract: (item: T) => string): string | null {
        if (items.length === 0) return null;
        const first = extract(items[0]);
        return items.every(item => extract(item) === first) ? first : null;
    }

    /** Convert API GameDayDto to display-ready GameDayLine. */
    private toGameDayLine(gd: GameDayDto): GameDayLine {
        const d = new Date(gd.date);
        const mm = String(d.getMonth() + 1).padStart(2, '0');
        const dd = String(d.getDate()).padStart(2, '0');
        const yyyy = d.getFullYear();

        return {
            dayNumber: gd.dayNumber,
            dow: gd.dow,
            dateFormatted: `${mm}/${dd}/${yyyy}`,
            fieldCount: gd.fieldCount,
            startTime: gd.startTime,
            endTime: gd.endTime,
            gsi: gd.gsi,
            totalSlots: gd.totalSlots
        };
    }

    onDeleteConfirmed(): void {
        this.deleteConfirmed.emit();
        this.deleteConfirmText.set('');
    }

    onDeleteCancelled(): void {
        this.deleteCancelled.emit();
        this.deleteConfirmText.set('');
    }

    requestReset(): void {
        this.showResetConfirm.set(true);
        this.resetConfirmText.set('');
        // Default: clear games + strategies + pairings but KEEP dates/field config
        this.resetGames.set(true);
        this.resetStrategyProfiles.set(true);
        this.resetPairings.set(true);
        this.resetDates.set(false);
        this.resetFieldTimeslots.set(false);
    }

    onResetConfirmed(): void {
        const options: DevResetOptions = {
            games: this.resetGames(),
            strategyProfiles: this.resetStrategyProfiles(),
            pairings: this.resetPairings(),
            dates: this.resetDates(),
            fieldTimeslots: this.resetFieldTimeslots(),
            sourceJobId: this.priorYearDefaults()?.priorJobId ?? undefined
        };
        this.showResetConfirm.set(false);
        this.resetConfirmText.set('');
        this.resetConfirmed.emit(options);
    }

    onResetCancelled(): void {
        this.showResetConfirm.set(false);
        this.resetConfirmText.set('');
    }
}
