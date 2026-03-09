/**
 * CalendarSectionComponent — Stepper Section ②
 *
 * Inline expandable section for calendar & structure configuration.
 * Displays a date×agegroup matrix where each cell is a rounds-per-day
 * dropdown (explicit round count, "Remaining" = null, or "—" = not playing).
 *
 * Per-division drill-down available for rare override scenarios.
 */

import { Component, ChangeDetectionStrategy, computed, effect, input, output, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupCanvasReadinessDto, PriorYearFieldDefaults, BulkDateAgegroupEntry } from '@core/api';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import type { CalendarApplyEvent, DateAssignment, ScheduleConfigValue } from './schedule-config.types';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

// ── Local types ──

interface DateChip {
    isoDate: string;
    dow: string;
    dateFormatted: string;
    /** True for dates added in this session (can be removed). False for existing dates from readiness data. */
    isManaged: boolean;
}

interface AgRow {
    agegroupId: string;
    agegroupName: string;
    teamCount: number;
    divisionCount: number;
    color?: string | null;
    /** Max rounds needed across all divisions (guarantee-driven). */
    maxRounds: number;
    wave: number;
    divisions: DivisionRow[];
}

interface DivisionRow {
    divId: string;
    divName: string;
    teamCount: number;
    /** Rounds for this division (guarantee-driven). */
    rounds: number;
}

/**
 * Cell value for the rounds-per-day matrix.
 * - undefined: not playing this day (equivalent to old "unchecked")
 * - null: "Remaining Rounds" — elastic day that absorbs leftover rounds
 * - number: explicit round count for this day
 */
type CellValue = number | null | undefined;

/** 2D cell map: cellMap[agegroupId][isoDate] = CellValue. */
type CellMap = Record<string, Record<string, CellValue>>;

/** Per-division override map: divOverrides[divisionId][isoDate] = CellValue. */
type DivOverrideMap = Record<string, Record<string, CellValue>>;

/** Dropdown option for round count selectors. */
interface RoundOption {
    value: CellValue;
    label: string;
}

@Component({
    selector: 'app-calendar-section',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent],
    templateUrl: './calendar-section.component.html',
    styleUrl: './calendar-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarSectionComponent {
    // ── Inputs ──
    readonly eventType = input<'league' | 'tournament'>('tournament');
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly priorYearDefaults = input<PriorYearFieldDefaults | null>(null);
    readonly priorYearRounds = input<Record<string, number> | null>(null);
    readonly configDates = input<ScheduleConfigValue<string[]>>({ value: [], source: 'default' });
    readonly configWaves = input<Record<string, number>>({});
    readonly configRoundsPerDay = input<Record<string, number>>({});
    readonly gameGuarantee = input<number | null>(null);
    readonly isExpanded = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly applyRequested = output<CalendarApplyEvent>();

    // ── UI state ──
    readonly isApplying = input(false);
    readonly showDatePicker = signal(false);
    readonly newDateValue = signal('');

    // ── Per-agegroup wave editing (per-AG, not per-date) ──
    readonly agWaveMap = signal<Record<string, number>>({});

    // ── 2D matrix editing state: rounds per day per agegroup ──
    readonly cellMap = signal<CellMap>({});

    // ── Per-division overrides (only populated when director explicitly overrides) ──
    readonly divOverrides = signal<DivOverrideMap>({});

    // ── Expanded agegroup drill-downs ──
    readonly expandedAgIds = signal<Set<string>>(new Set());

    // ── Date chips (managed dates for this session) ──
    readonly managedDates = signal<string[]>([]);

    // ── Helpers ──
    readonly contrastText = contrastText;

    constructor() {
        // Re-sync cellMap from readiness whenever readiness data changes.
        effect(() => {
            const initial = this.initialCellMap();
            this.cellMap.set(initial);

            // Clean managed dates that are now in the DB (readiness)
            const existingIsos = new Set(this.existingDates().map(d => d.isoDate));
            untracked(() => {
                this.managedDates.set(
                    this.managedDates().filter(d => !existingIsos.has(d))
                );
            });
        });
    }

    // ── Computed: initial cell map from readiness data ──

    readonly initialCellMap = computed((): CellMap => {
        const map = this.readinessMap();
        const ags = this.agegroups();
        const cfgRpd = this.configRoundsPerDay();
        const cfgDates = this.configDates();
        const result: CellMap = {};

        for (const ag of ags) {
            result[ag.agegroupId] = {};
            const readiness = map[ag.agegroupId];

            if (readiness?.gameDays && readiness.gameDays.length > 0) {
                // Existing readiness data: reconstruct cell values from round counts
                const activeDays = readiness.gameDays.filter(gd => gd.roundCount > 0);
                for (let i = 0; i < activeDays.length; i++) {
                    const gd = activeDays[i];
                    const dateKey = gd.date.substring(0, 10);
                    // Last active day defaults to null (Remaining) for backward compatibility
                    if (i === activeDays.length - 1) {
                        result[ag.agegroupId][dateKey] = null;
                    } else {
                        result[ag.agegroupId][dateKey] = gd.roundCount;
                    }
                }
            } else if (cfgRpd[ag.agegroupId] && cfgDates.value.length > 0) {
                // No readiness yet (new event): seed from prior-year/config defaults
                for (let i = 0; i < cfgDates.value.length; i++) {
                    const isoDate = cfgDates.value[i];
                    // Last day = Remaining, others get even distribution
                    if (i === cfgDates.value.length - 1) {
                        result[ag.agegroupId][isoDate] = null;
                    } else {
                        result[ag.agegroupId][isoDate] = cfgRpd[ag.agegroupId] ?? 1;
                    }
                }
            }
        }
        return result;
    });

    // ── Computed: existing dates from readiness ──

    readonly existingDates = computed((): DateChip[] => {
        const map = this.readinessMap();
        const dateSet = new Map<string, { dow: string }>();
        for (const r of Object.values(map)) {
            if (!r.isConfigured || !r.gameDays) continue;
            for (const gd of r.gameDays) {
                const key = gd.date.substring(0, 10);
                if (!dateSet.has(key)) {
                    dateSet.set(key, { dow: gd.dow });
                }
            }
        }
        return [...dateSet.entries()]
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([iso, info]) => {
                const d = new Date(iso + 'T00:00:00');
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                return { isoDate: iso, dow: info.dow, dateFormatted: `${mm}/${dd}/${d.getFullYear()}`, isManaged: false };
            });
    });

    // ── Computed: all dates (existing + newly added) ──

    readonly allDates = computed((): DateChip[] => {
        const existing = this.existingDates();
        const managed = this.managedDates();
        const existingIsos = new Set(existing.map(d => d.isoDate));

        const newChips: DateChip[] = managed
            .filter(iso => !existingIsos.has(iso))
            .map(iso => {
                const d = new Date(iso + 'T00:00:00');
                const dow = d.toLocaleDateString('en-US', { weekday: 'short' });
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                return { isoDate: iso, dow, dateFormatted: `${mm}/${dd}/${d.getFullYear()}`, isManaged: true };
            });

        return [...existing, ...newChips].sort((a, b) => a.isoDate.localeCompare(b.isoDate));
    });

    readonly dateCount = computed(() => this.allDates().length);

    // ── Computed: collapsed summary label ──

    readonly summaryLabel = computed((): string => {
        const dates = this.existingDates();
        if (dates.length === 0) return 'Dates needed';
        if (dates.length === 1) return `${dates[0].dow} ${dates[0].dateFormatted}`;
        const first = dates[0];
        const last = dates[dates.length - 1];
        return `${dates.length} days · ${first.dow} ${first.dateFormatted} – ${last.dow} ${last.dateFormatted}`;
    });

    readonly isComplete = computed(() => this.existingDates().length > 0);

    // ── Computed: source badge ──

    readonly sourceBadge = computed((): string | null => {
        const cfg = this.configDates();
        if (cfg.source === 'prior-year' && cfg.sourceLabel) return cfg.sourceLabel;
        return null;
    });

    // ── Computed: AG rows ──

    readonly agRows = computed((): AgRow[] => {
        const ags = this.agegroups();
        const map = this.readinessMap();
        const pyRounds = this.priorYearRounds();
        const waveMap = this.agWaveMap();
        const cfgWaves = this.configWaves();
        const guarantee = this.gameGuarantee();

        return ags
            .slice()
            .sort((a, b) => (a.sortAge ?? 0) - (b.sortAge ?? 0))
            .map(ag => {
                const teamCount = agTeamCount(ag);
                const maxRounds = this.computeMaxDivisionRounds(ag, map[ag.agegroupId], pyRounds, guarantee);

                const divisions: DivisionRow[] = ag.divisions.map(div => ({
                    divId: div.divId,
                    divName: div.divName,
                    teamCount: div.teamCount,
                    rounds: this.computeDivisionRounds(div.teamCount, guarantee)
                }));

                return {
                    agegroupId: ag.agegroupId,
                    agegroupName: ag.agegroupName,
                    teamCount,
                    divisionCount: ag.divisions.length,
                    color: ag.color,
                    maxRounds,
                    wave: waveMap[ag.agegroupId]
                        ?? cfgWaves[ag.agegroupId]
                        ?? 1,
                    divisions
                };
            });
    });

    // ── Computed: dropdown options per agegroup ──

    roundOptions(maxRounds: number): RoundOption[] {
        const opts: RoundOption[] = [
            { value: undefined, label: '—' }
        ];
        for (let i = 1; i <= maxRounds; i++) {
            opts.push({ value: i, label: String(i) });
        }
        opts.push({ value: null, label: 'Remaining' });
        return opts;
    }

    // ── Computed: true when any cell, date, or wave differs from the saved DB state ──

    readonly hasPendingChanges = computed(() => {
        const cells = this.cellMap();
        const initial = this.initialCellMap();
        const dates = this.allDates();
        const existingIsos = new Set(this.existingDates().map(d => d.isoDate));

        // Wave changes
        const waveMap = this.agWaveMap();
        const cfgWaves = this.configWaves();
        for (const agId of Object.keys(waveMap)) {
            if (waveMap[agId] !== (cfgWaves[agId] ?? 1)) return true;
        }

        // Cell changes per date
        for (const d of dates) {
            if (!existingIsos.has(d.isoDate)) {
                // Managed (new) date: any assigned cell = pending change
                for (const agId of Object.keys(cells)) {
                    if (cells[agId]?.[d.isoDate] !== undefined) return true;
                }
                continue;
            }

            // Existing date: check each agegroup for diff
            const allAgIds = new Set([...Object.keys(cells), ...Object.keys(initial)]);
            for (const agId of allAgIds) {
                const current = cells[agId]?.[d.isoDate];
                const was = initial[agId]?.[d.isoDate];
                if (current !== was) return true;
            }
        }

        // Division override changes
        const overrides = this.divOverrides();
        if (Object.keys(overrides).length > 0) return true;

        return false;
    });

    // ── Validation: exactly one NULL day per agegroup that has assigned days ──

    readonly validationErrors = computed((): string[] => {
        const cells = this.cellMap();
        const rows = this.agRows();
        const errors: string[] = [];

        for (const row of rows) {
            const agCells = cells[row.agegroupId] ?? {};
            const assignedDays = Object.entries(agCells).filter(([, v]) => v !== undefined);
            if (assignedDays.length === 0) continue;

            const nullDays = assignedDays.filter(([, v]) => v === null);
            if (nullDays.length === 0) {
                errors.push(`${row.agegroupName}: must have one "Remaining" day`);
            } else if (nullDays.length > 1) {
                errors.push(`${row.agegroupName}: only one day can be "Remaining"`);
            }

            // Check explicit rounds don't exceed minimum division rounds
            const explicitSum = assignedDays
                .filter(([, v]) => typeof v === 'number')
                .reduce((sum, [, v]) => sum + (v as number), 0);
            const minDivRounds = Math.min(...row.divisions.map(d => d.rounds));
            if (explicitSum >= minDivRounds && nullDays.length > 0) {
                errors.push(`${row.agegroupName}: explicit rounds (${explicitSum}) leave nothing for "Remaining" day`);
            }
        }

        return errors;
    });

    readonly isValid = computed(() => this.validationErrors().length === 0);

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    onAddDate(): void {
        this.newDateValue.set('');
        this.showDatePicker.set(true);
    }

    onDatePicked(): void {
        const val = this.newDateValue();
        if (!val) return;
        const current = this.managedDates();
        if (!current.includes(val)) {
            this.managedDates.set([...current, val].sort());
        }
        this.newDateValue.set('');
        this.showDatePicker.set(false);
    }

    onDatePickerCancelled(): void {
        this.newDateValue.set('');
        this.showDatePicker.set(false);
    }

    removeDate(isoDate: string): void {
        this.managedDates.set(this.managedDates().filter(d => d !== isoDate));
    }

    setWave(agId: string, wave: number): void {
        this.agWaveMap.set({ ...this.agWaveMap(), [agId]: wave });
    }

    cycleWave(agId: string, current: number): void {
        const next = current >= 3 ? 1 : current + 1;
        this.setWave(agId, next);
    }

    // ── Cell value access ──

    getCellValue(agId: string, isoDate: string): CellValue {
        return this.cellMap()[agId]?.[isoDate];
    }

    /** Get string for binding to <select>. Converts undefined→'off', null→'remaining', number→string. */
    getCellSelectValue(agId: string, isoDate: string): string {
        const v = this.getCellValue(agId, isoDate);
        if (v === undefined) return 'off';
        if (v === null) return 'remaining';
        return String(v);
    }

    onCellChange(agId: string, isoDate: string, selectValue: string): void {
        const cells = { ...this.cellMap() };
        const agCells = { ...(cells[agId] ?? {}) };

        if (selectValue === 'off') {
            delete agCells[isoDate];
        } else if (selectValue === 'remaining') {
            agCells[isoDate] = null;
            // If 2-day event, this is auto-enforced — clear any other NULL for this agegroup
            this.enforceOneNull(agCells, isoDate);
        } else {
            agCells[isoDate] = Number(selectValue);
        }

        cells[agId] = agCells;
        this.cellMap.set(cells);

        // For 2-day events: if this was the first day assigned, auto-set the other to Remaining
        this.autoAssignRemaining(cells, agId);
    }

    // ── Division drill-down ──

    isAgExpanded(agId: string): boolean {
        return this.expandedAgIds().has(agId);
    }

    toggleAgExpand(agId: string): void {
        const current = new Set(this.expandedAgIds());
        if (current.has(agId)) {
            current.delete(agId);
        } else {
            current.add(agId);
        }
        this.expandedAgIds.set(current);
    }

    /** Get division cell value: override if set, otherwise inherited from agegroup. */
    getDivCellValue(divId: string, agId: string, isoDate: string): CellValue {
        const override = this.divOverrides()[divId]?.[isoDate];
        if (override !== undefined || this.divOverrides()[divId]?.hasOwnProperty(isoDate)) {
            return this.divOverrides()[divId][isoDate];
        }
        return this.getCellValue(agId, isoDate);
    }

    getDivCellSelectValue(divId: string, agId: string, isoDate: string): string {
        const v = this.getDivCellValue(divId, agId, isoDate);
        if (v === undefined) return 'off';
        if (v === null) return 'remaining';
        return String(v);
    }

    isDivOverridden(divId: string): boolean {
        return Object.keys(this.divOverrides()[divId] ?? {}).length > 0;
    }

    onDivCellChange(divId: string, isoDate: string, selectValue: string): void {
        const overrides = { ...this.divOverrides() };
        const divCells = { ...(overrides[divId] ?? {}) };

        if (selectValue === 'off') {
            divCells[isoDate] = undefined;
        } else if (selectValue === 'remaining') {
            divCells[isoDate] = null;
        } else {
            divCells[isoDate] = Number(selectValue);
        }

        overrides[divId] = divCells;
        this.divOverrides.set(overrides);
    }

    resetDivOverride(divId: string): void {
        const overrides = { ...this.divOverrides() };
        delete overrides[divId];
        this.divOverrides.set(overrides);
    }

    // ── Apply ──

    onApply(): void {
        const cells = this.cellMap();
        const initial = this.initialCellMap();
        const dates = this.allDates();
        const rows = this.agRows();

        if (dates.length === 0 || rows.length === 0) return;

        const assignments: Record<string, DateAssignment> = {};

        for (const d of dates) {
            const entries: BulkDateAgegroupEntry[] = [];
            const removedIds: string[] = [];

            for (const row of rows) {
                const cellVal = cells[row.agegroupId]?.[d.isoDate];
                const wasAssigned = initial[row.agegroupId]?.[d.isoDate] !== undefined;
                const isAssigned = cellVal !== undefined;

                if (isAssigned) {
                    // Resolve roundsPerDay: explicit number, or compute for NULL (remaining)
                    let rpd: number;
                    if (typeof cellVal === 'number') {
                        rpd = cellVal;
                    } else {
                        // NULL = remaining: total rounds minus sum of explicit days
                        const explicitSum = Object.entries(cells[row.agegroupId] ?? {})
                            .filter(([, v]) => typeof v === 'number')
                            .reduce((sum, [, v]) => sum + (v as number), 0);
                        rpd = Math.max(1, row.maxRounds - explicitSum);
                    }

                    entries.push({
                        agegroupId: row.agegroupId,
                        wave: row.wave,
                        roundsPerDay: rpd
                    });
                } else if (wasAssigned) {
                    removedIds.push(row.agegroupId);
                }
            }

            if (entries.length > 0 || removedIds.length > 0) {
                assignments[d.isoDate] = {
                    entries,
                    removedAgegroupIds: removedIds.length > 0 ? removedIds : undefined
                };
            }
        }

        // Build waveMap from current state
        const waveMap: Record<string, number> = {};
        for (const row of rows) {
            waveMap[row.agegroupId] = row.wave;
        }

        this.applyRequested.emit({ assignments, waveMap });
    }

    // ── Private helpers ──

    /**
     * Guarantee-driven round count for a single division.
     * Even teams: guarantee rounds. Odd teams: guarantee + 1 (bye tax).
     */
    private computeDivisionRounds(teamCount: number, guarantee: number | null): number {
        const g = guarantee ?? 3;
        if (teamCount <= 1) return 0;
        return teamCount % 2 === 0 ? g : g + 1;
    }

    /**
     * Max rounds needed across all divisions in an agegroup (guarantee-driven).
     * Priority: pairings (authoritative) → prior year → guarantee formula.
     */
    private computeMaxDivisionRounds(
        ag: AgegroupWithDivisionsDto,
        readiness: AgegroupCanvasReadinessDto | undefined,
        pyRounds: Record<string, number> | null,
        guarantee: number | null
    ): number {
        // 1. Pairings — authoritative, already the max across divisions
        if (readiness && readiness.maxPairingRound > 0) {
            return readiness.maxPairingRound;
        }
        // 2. Prior year
        if (pyRounds && pyRounds[ag.agegroupName] != null) {
            return pyRounds[ag.agegroupName];
        }
        // 3. Guarantee-driven formula: max across individual divisions
        let maxRounds = 0;
        for (const div of ag.divisions) {
            const rounds = this.computeDivisionRounds(div.teamCount, guarantee);
            maxRounds = Math.max(maxRounds, rounds);
        }
        return maxRounds;
    }

    /** Ensure only one NULL (Remaining) day per agegroup. When setting a new NULL, clear any previous one. */
    private enforceOneNull(agCells: Record<string, CellValue>, keepDate: string): void {
        for (const [date, val] of Object.entries(agCells)) {
            if (date !== keepDate && val === null) {
                // Demote previous NULL to 1 round
                agCells[date] = 1;
            }
        }
    }

    /** For 2-day events: when director sets the first day, auto-assign the other as Remaining. */
    private autoAssignRemaining(cells: CellMap, agId: string): void {
        const dates = this.allDates();
        if (dates.length !== 2) return;

        const agCells = cells[agId] ?? {};
        const assigned = Object.entries(agCells).filter(([, v]) => v !== undefined);

        // If exactly one day is set to a number (not null/remaining), auto-set the other as Remaining
        if (assigned.length === 1 && typeof assigned[0][1] === 'number') {
            const otherDate = dates.find(d => d.isoDate !== assigned[0][0]);
            if (otherDate) {
                const updated = { ...cells };
                updated[agId] = { ...agCells, [otherDate.isoDate]: null };
                this.cellMap.set(updated);
            }
        }
    }
}
