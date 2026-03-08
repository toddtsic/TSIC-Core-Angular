/**
 * TimeConfigSectionComponent — Stepper Section ③
 *
 * Agegroup × Date matrix for per-AG-per-day time configuration:
 *   - GSI (Min Between Games) — per-agegroup column (not per-date)
 *   - Start Time — per-agegroup-per-date cell
 *   - Max Games/Field — per-agegroup-per-date cell
 *   - Capacity summary (auto-calculated)
 *
 * The matrix mirrors Calendar Section ②'s date×agegroup grid, giving
 * schedulers a consistent mental model. GSI stays per-row because game
 * duration doesn't change based on which day it is.
 */

import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupCanvasReadinessDto, AgDowFieldConfigEntry, PriorYearFieldDefaults } from '@core/api';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import { contrastText } from '../../../shared/utils/scheduling-helpers';

// ── Emitted when user clicks "Save" ──

export interface TimeConfigSaveEvent {
    /** GSI scope: 'same' = uniform, 'per-ag' = per-agegroup values */
    gsiScope: 'same' | 'per-ag';
    gsi: number | Record<string, number>;
    /** Uniform defaults for backward compat (used when no matrix overrides exist) */
    startTime: string;
    maxGamesPerField: number;
    /** Per-agegroup-per-DOW overrides from the matrix. */
    agDowOverrides: AgDowFieldConfigEntry[];
}

// ── Date column descriptor (passed from parent/calendar section) ──

export interface DateColumnInfo {
    isoDate: string;
    dow: string;
    dateFormatted: string;
}

// ── Per-cell editable values ──

interface MatrixCellValue {
    startTime: string;
    maxGames: number;
}

// ── Matrix row (one per agegroup) ──

interface MatrixRow {
    agegroupId: string;
    agegroupName: string;
    color?: string | null;
    gsi: number;
    gsiSource: string;
    /** Per-date cell values, keyed by ISO date */
    cells: Record<string, MatrixCellValue>;
}

/** Canonical DOW order for sorting */
const DOW_ORDER: Record<string, number> = {
    sunday: 0, monday: 1, tuesday: 2, wednesday: 3,
    thursday: 4, friday: 5, saturday: 6
};

@Component({
    selector: 'app-time-config-section',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './time-config-section.component.html',
    styleUrl: './time-config-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TimeConfigSectionComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly priorYearDefaults = input<PriorYearFieldDefaults | null>(null);
    readonly assignedFieldCount = input(0);
    readonly isExpanded = input(false);
    readonly allDates = input<DateColumnInfo[]>([]);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly saveRequested = output<TimeConfigSaveEvent>();

    readonly isSaving = input(false);

    // ── Helpers ──
    readonly contrastText = contrastText;

    // ── Editing state ──

    /** Per-AG GSI overrides (agegroup-level, not per-date) */
    readonly localAgGsiMap = signal<Record<string, number>>({});

    /** Per-AG-per-date cell overrides: agId → isoDate → { startTime, maxGames } */
    readonly localCellMap = signal<Record<string, Record<string, MatrixCellValue>>>({});

    /** Bulk-fill defaults for "Apply All" */
    readonly bulkStartTime = signal('');
    readonly bulkMaxGames = signal<number | null>(null);

    readonly bulkGsi = signal<number | null>(null);

    // ── Computed: configured agegroups ──

    readonly configuredAgegroups = computed(() =>
        Object.values(this.readinessMap()).filter(r => r.isConfigured)
    );

    // ── Computed: default values from DB or prior year ──

    readonly defaultGsi = computed((): number => {
        const configured = this.configuredAgegroups();
        if (configured.length > 0 && configured[0].gamestartInterval != null) {
            return configured[0].gamestartInterval!;
        }
        return this.priorYearDefaults()?.gamestartInterval ?? 60;
    });

    readonly defaultStartTime = computed((): string => {
        const configured = this.configuredAgegroups();
        if (configured.length > 0 && configured[0].startTime) {
            return configured[0].startTime!;
        }
        return this.priorYearDefaults()?.startTime ?? '8:00 AM';
    });

    readonly defaultMaxGames = computed((): number => {
        const configured = this.configuredAgegroups();
        if (configured.length > 0 && configured[0].maxGamesPerField != null) {
            return configured[0].maxGamesPerField!;
        }
        return this.priorYearDefaults()?.maxGamesPerField ?? 8;
    });

    // ── Computed: source badge ──

    readonly sourceLabel = computed((): string | null => {
        const py = this.priorYearDefaults();
        const configured = this.configuredAgegroups();
        if (configured.length > 0) return null;
        if (py?.priorJobName) return `From ${py.priorJobName} ${py.priorYear ?? ''}`.trim();
        return null;
    });

    // ── Computed: matrix rows ──

    readonly matrixRows = computed((): MatrixRow[] => {
        const ags = this.agegroups();
        const map = this.readinessMap();
        const py = this.priorYearDefaults();
        const dates = this.allDates();
        const localGsi = this.localAgGsiMap();
        const localCells = this.localCellMap();
        const defGsi = this.defaultGsi();
        const defStartTime = this.defaultStartTime();
        const defMaxGames = this.defaultMaxGames();

        return ags.slice()
            .sort((a, b) => (a.sortAge ?? 0) - (b.sortAge ?? 0))
            .map(ag => {
                const r = map[ag.agegroupId];
                const hasConfig = r?.isConfigured ?? false;

                // GSI: local edit → DB → prior year → default
                let gsi: number;
                let gsiSource: string;
                if (localGsi[ag.agegroupId] != null) {
                    gsi = localGsi[ag.agegroupId];
                    gsiSource = 'edited';
                } else if (hasConfig && r.gamestartInterval != null) {
                    gsi = r.gamestartInterval!;
                    gsiSource = 'current';
                } else if (py?.gamestartInterval != null) {
                    gsi = py.gamestartInterval;
                    gsiSource = 'prior year';
                } else {
                    gsi = defGsi;
                    gsiSource = 'default';
                }

                // Build cells for each date
                const cells: Record<string, MatrixCellValue> = {};
                for (const d of dates) {
                    const localCell = localCells[ag.agegroupId]?.[d.isoDate];
                    if (localCell) {
                        cells[d.isoDate] = localCell;
                        continue;
                    }

                    // Look up DB readiness for this date
                    const gameDay = (r?.gameDays ?? []).find(gd =>
                        gd.date.substring(0, 10) === d.isoDate
                    );
                    // maxGamesPerField is an agegroup-level config value;
                    // GameDayDto.totalSlots is an aggregate (sum across fields) — don't derive from it
                    const agMaxGames = r?.maxGamesPerField ?? defMaxGames;
                    if (gameDay && gameDay.startTime) {
                        cells[d.isoDate] = {
                            startTime: gameDay.startTime,
                            maxGames: agMaxGames
                        };
                    } else {
                        cells[d.isoDate] = {
                            startTime: defStartTime,
                            maxGames: agMaxGames
                        };
                    }
                }

                return {
                    agegroupId: ag.agegroupId,
                    agegroupName: ag.agegroupName,
                    color: ag.color,
                    gsi,
                    gsiSource,
                    cells
                };
            });
    });

    readonly dateCount = computed(() => this.allDates().length);

    // ── Computed: collapsed summary ──

    readonly summaryLabel = computed((): string => {
        const gsi = this.defaultGsi();
        const st = this.defaultStartTime();
        const max = this.defaultMaxGames();
        const dates = this.allDates();
        if (dates.length === 0) return `${gsi} min · Start: ${st} · Max: ${max}/field`;
        return `${gsi} min · ${dates.length} day${dates.length !== 1 ? 's' : ''} configured`;
    });

    readonly isComplete = computed(() => true);

    // ── Computed: capacity summary ──

    readonly capacitySummary = computed((): string => {
        const fields = this.assignedFieldCount();
        const maxGames = this.defaultMaxGames();
        const gsi = this.defaultGsi();
        if (fields === 0) return 'No fields assigned';
        const totalSlots = fields * maxGames;
        const hours = Math.round((maxGames * gsi) / 60 * 10) / 10;
        return `${fields} field${fields !== 1 ? 's' : ''} × ${maxGames} rows × ${gsi} min = ${totalSlots} slots/day (~${hours} hrs)`;
    });

    // ── Computed: dirty tracking ──

    readonly isDirty = computed(() => {
        return Object.keys(this.localAgGsiMap()).length > 0
            || Object.keys(this.localCellMap()).length > 0;
    });

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    setAgGsi(agId: string, value: number): void {
        this.localAgGsiMap.set({ ...this.localAgGsiMap(), [agId]: value });
    }

    setCellStartTime(agId: string, isoDate: string, value: string): void {
        const map = { ...this.localCellMap() };
        const row = this.matrixRows().find(r => r.agegroupId === agId);
        const current = row?.cells[isoDate];
        map[agId] = {
            ...(map[agId] ?? {}),
            [isoDate]: { startTime: value, maxGames: current?.maxGames ?? this.defaultMaxGames() }
        };
        this.localCellMap.set(map);
    }

    setCellMaxGames(agId: string, isoDate: string, value: number | null): void {
        if (value == null) return;
        const map = { ...this.localCellMap() };
        const row = this.matrixRows().find(r => r.agegroupId === agId);
        const current = row?.cells[isoDate];
        map[agId] = {
            ...(map[agId] ?? {}),
            [isoDate]: { startTime: current?.startTime ?? this.defaultStartTime(), maxGames: value }
        };
        this.localCellMap.set(map);
    }

    /** Apply bulk values to all cells and optionally GSI */
    applyBulkFill(): void {
        const st = this.bulkStartTime();
        const max = this.bulkMaxGames();
        const gsi = this.bulkGsi();
        if (!st && max == null && gsi == null) return;

        const rows = this.matrixRows();
        const dates = this.allDates();

        // Apply GSI to all agegroups if set
        if (gsi != null && gsi > 0) {
            const gsiMap: Record<string, number> = {};
            for (const row of rows) gsiMap[row.agegroupId] = gsi;
            this.localAgGsiMap.set(gsiMap);
        }

        // Apply start time / max games to all cells if set
        if (st || max != null) {
            const map: Record<string, Record<string, MatrixCellValue>> = {};
            for (const row of rows) {
                map[row.agegroupId] = {};
                for (const d of dates) {
                    const current = row.cells[d.isoDate];
                    map[row.agegroupId][d.isoDate] = {
                        startTime: st || current?.startTime || this.defaultStartTime(),
                        maxGames: max ?? current?.maxGames ?? this.defaultMaxGames()
                    };
                }
            }
            this.localCellMap.set(map);
        }
    }

    /** Safe integer parse */
    safeInt(value: unknown): number | null {
        const n = Number(value);
        return Number.isFinite(n) && n > 0 ? Math.round(n) : null;
    }

    onSave(): void {
        const rows = this.matrixRows();
        const dates = this.allDates();

        // Determine GSI scope
        const gsiValues = new Set(rows.map(r => r.gsi));
        const gsiScope: 'same' | 'per-ag' = gsiValues.size <= 1 ? 'same' : 'per-ag';

        let gsi: number | Record<string, number>;
        if (gsiScope === 'per-ag') {
            const map: Record<string, number> = {};
            for (const row of rows) map[row.agegroupId] = row.gsi;
            gsi = map;
        } else {
            gsi = rows.length > 0 ? rows[0].gsi : this.defaultGsi();
        }

        // Build per-AG-per-DOW overrides from the matrix cells
        // Group by (agId, dow) and take the cell values
        const agDowOverrides: AgDowFieldConfigEntry[] = [];
        const seen = new Set<string>();

        for (const row of rows) {
            for (const d of dates) {
                const cell = row.cells[d.isoDate];
                if (!cell) continue;
                const key = `${row.agegroupId}|${d.dow}`;
                if (seen.has(key)) continue; // same AG+DOW already added
                seen.add(key);
                agDowOverrides.push({
                    agegroupId: row.agegroupId,
                    dow: d.dow,
                    startTime: cell.startTime,
                    maxGamesPerField: cell.maxGames
                });
            }
        }

        // Uniform defaults (first row first cell as representative)
        const firstCell = rows.length > 0 && dates.length > 0
            ? rows[0].cells[dates[0].isoDate]
            : null;

        this.saveRequested.emit({
            gsiScope,
            gsi,
            startTime: firstCell?.startTime ?? this.defaultStartTime(),
            maxGamesPerField: firstCell?.maxGames ?? this.defaultMaxGames(),
            agDowOverrides
        });
    }
}
