/**
 * Schedule Configuration Types
 *
 * These types match the flowchart's ScheduleAgentConfig 1:1.
 * Every flowchart node maps to a config field with provenance tracking.
 */

// ── Provenance wrapper ──

export type ConfigSource = 'saved' | 'current' | 'prior-year' | 'default';

export interface ScheduleConfigValue<T> {
    value: T;
    source: ConfigSource;
    sourceLabel?: string; // e.g. "Summer 2025 Tournament" for prior-year
}

// ── Main config interface ──

export interface ScheduleConfig {
    jobId: string;
    eventType: 'league' | 'tournament';

    // ── Calendar (Section ②) ──
    dates: ScheduleConfigValue<string[]>; // ISO dates

    // League-specific (Nodes 2L–5L)
    dow?: ScheduleConfigValue<string>;          // "Saturday"
    skipWeeks?: ScheduleConfigValue<string[]>;   // ISO dates of holidays/breaks

    // Tournament structure (Nodes 2T–8T)
    agDaySpan?: ScheduleConfigValue<'single' | 'multi' | 'mix'>;
    divDaySpan?: ScheduleConfigValue<'same' | 'different'>;
    agToDayMap?: ScheduleConfigValue<Record<string, string>>;    // agId → ISO date
    roundDistribution?: ScheduleConfigValue<Record<string, Record<string, number>>>; // agId → date → rounds

    // ── Fields (Section ①, Nodes C1–C2) ──
    fieldIds: ScheduleConfigValue<string[]>;
    fieldMappingScope: ScheduleConfigValue<'shared' | 'per-ag' | 'per-div'>;
    fieldMapping?: ScheduleConfigValue<Record<string, string[]>>; // agId or divId → fieldId[]

    // ── Time Config (Section ③, Nodes C3–C5) ──
    gsiScope: ScheduleConfigValue<'same' | 'per-ag'>;
    gsi: ScheduleConfigValue<number | Record<string, number>>;
    startTimeScope: ScheduleConfigValue<'same' | 'per-ag'>;
    startTime: ScheduleConfigValue<string | Record<string, string>>;
    maxGamesPerField: ScheduleConfigValue<number>;

    // ── Rounds ──
    roundsPerAg: Record<string, ScheduleConfigValue<number>>; // agId → rounds needed

    // ── Wave & R/day (Calendar section state, persisted across sections) ──
    waveAssignments: Record<string, number>;   // agegroupId → wave (1-3)
    roundsPerDay: Record<string, number>;      // agegroupId → R/day

    // ── Strategy (Section ④) ──
    placement: ScheduleConfigValue<number>;
    gapPattern: ScheduleConfigValue<number>;

    // ── League-specific rules (Node 5L) ──
    oddDivByeHandling?: ScheduleConfigValue<boolean>; // bPlayOddDivisionByeTeam
}

// ── Freshness tracking for cascading invalidation ──

export type Freshness = 'valid' | 'stale' | 'missing';

// ── Stepper section expand/collapse state ──

export type StepperSection = 'fields' | 'calendar' | 'timeConfig' | 'strategy' | 'pairings' | 'build';

// ── Scope for scoped operations ──

export type SchedulingScope = {
    level: 'event' | 'agegroup' | 'division';
    agegroupId?: string;
    divisionId?: string;
};

// ── localStorage schema for Scenario B ──

export interface SavedScheduleConfig extends ScheduleConfig {
    savedAt: string; // ISO timestamp
}

// ── Calendar Apply Event (emitted by CalendarSectionComponent) ──

import type { BulkDateAgegroupEntry } from '@core/api';

/** Per-date assignment payload for a single game date. */
export interface DateAssignment {
    /** Agegroups to assign/update on this date (rounds > 0). */
    entries: BulkDateAgegroupEntry[];
    /** Agegroups to UN-assign from this date (cell changed to "—"). */
    removedAgegroupIds?: string[];
}

/** Emitted when user clicks "Save" in the field config section.
 *  Contains only agegroups with fewer than all event fields (overrides). */
export interface FieldConfigApplyEvent {
    /** agegroupId → fieldId[]. Only AGs with overrides (fewer than all event fields). */
    overrides: Record<string, string[]>;
}

/** Emitted when user clicks "Save & Apply" in the calendar section.
 *  GSI/StartTime/MaxGames are NOT included — the parent reads current effective
 *  values from readiness data or config service when calling bulkAssignDate. */
export interface CalendarApplyEvent {
    /** ISO date key → per-date entries + removals. Only dates with changes included. */
    assignments: Record<string, DateAssignment>;
    /** agegroupId → wave (1-3). Per-agegroup, not per-date. */
    waveMap: Record<string, number>;
}
