/**
 * CalendarSectionComponent — Stepper Section ②
 *
 * Inline expandable section for calendar & structure configuration.
 * Displays a date×agegroup matrix where each cell is a checkbox
 * (checked = agegroup plays on that date). The system computes
 * roundsPerDay from division structure when applying.
 */

import { Component, ChangeDetectionStrategy, computed, effect, input, output, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupCanvasReadinessDto, PriorYearFieldDefaults, BulkDateAgegroupEntry } from '@core/api';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import type { CalendarApplyEvent, DateAssignment, ScheduleConfigValue } from './schedule-config.types';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';

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
    /** Max rounds needed across all divisions (for roundsPerDay computation). */
    maxRounds: number;
    wave: number;
}

/** 2D cell map: cellMap[agegroupId][isoDate] = plays on that date. */
type CellMap = Record<string, Record<string, boolean>>;

@Component({
    selector: 'app-calendar-section',
    standalone: true,
    imports: [CommonModule, FormsModule],
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

    // ── 2D matrix editing state: checked dates per agegroup ──
    readonly cellMap = signal<CellMap>({});

    // ── Date chips (managed dates for this session) ──
    readonly managedDates = signal<string[]>([]);

    // ── Helpers ──
    readonly contrastText = contrastText;

    constructor() {
        // Re-sync cellMap from readiness whenever readiness data changes.
        // This ensures the matrix reflects the latest DB state after each apply,
        // and cleans managed dates that are now persisted.
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
                // Existing readiness data: any date with rounds = checked
                for (const gd of readiness.gameDays) {
                    const dateKey = gd.date.substring(0, 10);
                    if (gd.roundCount > 0) {
                        result[ag.agegroupId][dateKey] = true;
                    }
                }
            } else if (cfgRpd[ag.agegroupId] && cfgDates.value.length > 0) {
                // No readiness yet (new event): seed from prior-year/config defaults
                for (const isoDate of cfgDates.value) {
                    result[ag.agegroupId][isoDate] = true;
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

        return ags
            .slice()
            .sort((a, b) => (a.sortAge ?? 0) - (b.sortAge ?? 0))
            .map(ag => {
                const teamCount = agTeamCount(ag);
                const maxRounds = this.computeMaxDivisionRounds(ag, map[ag.agegroupId], pyRounds);

                return {
                    agegroupId: ag.agegroupId,
                    agegroupName: ag.agegroupName,
                    teamCount,
                    divisionCount: ag.divisions.length,
                    color: ag.color,
                    maxRounds,
                    wave: waveMap[ag.agegroupId]
                        ?? cfgWaves[ag.agegroupId]
                        ?? 1
                };
            });
    });

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
                // Managed (new) date: any checked cell = pending change
                for (const agId of Object.keys(cells)) {
                    if (cells[agId]?.[d.isoDate]) return true;
                }
                continue;
            }

            // Existing date: check each agegroup for diff
            const allAgIds = new Set([...Object.keys(cells), ...Object.keys(initial)]);
            for (const agId of allAgIds) {
                const current = !!cells[agId]?.[d.isoDate];
                const was = !!initial[agId]?.[d.isoDate];
                if (current !== was) return true;
            }
        }

        return false;
    });

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    onAddDate(): void {
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

    isCellChecked(agId: string, isoDate: string): boolean {
        return !!this.cellMap()[agId]?.[isoDate];
    }

    toggleCell(agId: string, isoDate: string): void {
        const cells = { ...this.cellMap() };
        const current = !!cells[agId]?.[isoDate];
        cells[agId] = { ...(cells[agId] ?? {}), [isoDate]: !current };
        this.cellMap.set(cells);
    }

    onApply(): void {
        const cells = this.cellMap();
        const initial = this.initialCellMap();
        const dates = this.allDates();
        const rows = this.agRows();
        const isLeague = this.eventType() === 'league';

        if (dates.length === 0 || rows.length === 0) return;

        const assignments: Record<string, DateAssignment> = {};

        for (const d of dates) {
            const entries: BulkDateAgegroupEntry[] = [];
            const removedIds: string[] = [];

            for (const row of rows) {
                const isChecked = !!cells[row.agegroupId]?.[d.isoDate];
                const wasChecked = !!initial[row.agegroupId]?.[d.isoDate];

                if (isChecked) {
                    // Compute roundsPerDay: distribute max division rounds across checked dates.
                    // Even distribution for now — per-day granularity comes from Time config.
                    const checkedDates = dates.filter(dd => cells[row.agegroupId]?.[dd.isoDate]).length;
                    const rpd = isLeague ? 1 : Math.max(1, Math.ceil(row.maxRounds / Math.max(1, checkedDates)));

                    entries.push({
                        agegroupId: row.agegroupId,
                        wave: row.wave,
                        roundsPerDay: rpd
                    });
                } else if (wasChecked) {
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
     * Max rounds needed across all divisions in an agegroup.
     * Priority: pairings (authoritative) → prior year → per-division formula.
     */
    private computeMaxDivisionRounds(
        ag: AgegroupWithDivisionsDto,
        readiness: AgegroupCanvasReadinessDto | undefined,
        pyRounds: Record<string, number> | null
    ): number {
        // 1. Pairings — authoritative, already the max across divisions
        if (readiness && readiness.maxPairingRound > 0) {
            return readiness.maxPairingRound;
        }
        // 2. Prior year
        if (pyRounds && pyRounds[ag.agegroupName] != null) {
            return pyRounds[ag.agegroupName];
        }
        // 3. Formula: max rounds across individual divisions
        let maxRounds = 0;
        for (const div of ag.divisions) {
            if (div.teamCount <= 1) continue;
            const rounds = div.teamCount % 2 === 0 ? div.teamCount - 1 : div.teamCount;
            maxRounds = Math.max(maxRounds, rounds);
        }
        return maxRounds;
    }
}
