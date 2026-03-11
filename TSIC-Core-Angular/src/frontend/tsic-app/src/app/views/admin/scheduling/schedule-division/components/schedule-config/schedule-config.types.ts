/**
 * Schedule Configuration Types
 *
 * These types support the ScheduleConfigService (localStorage-backed build config).
 * The config service is slated for removal once the engine reads DivisionProcessingOrder
 * from DB directly — at that point, these types can be deleted too.
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

    // ── Game Days (Section ①) ──
    dates: ScheduleConfigValue<string[]>; // ISO dates (union of all agegroup dates)

    // ── Per-agegroup projected dates (from prior year projection) ──
    // agegroupId → { isoDate, rounds, dow }[]
    projectedDates?: ScheduleConfigValue<Record<string, { date: string; rounds: number; dow: string }[]>>;

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

    // ── Per-day field assignments (derived from prior year game records) ──
    // agegroupId → { dow → fieldName[] } — e.g. { "Saturday": ["Field 1", "Field 2"] }
    fieldsByDay?: ScheduleConfigValue<Record<string, Record<string, string[]>>>;

    // ── Time Config (Section ③, Nodes C3–C5) ──
    gsiScope: ScheduleConfigValue<'same' | 'per-ag'>;
    gsi: ScheduleConfigValue<number | Record<string, number>>;
    startTimeScope: ScheduleConfigValue<'same' | 'per-ag'>;
    startTime: ScheduleConfigValue<string | Record<string, string>>;
    maxGamesPerField: ScheduleConfigValue<number>;

    // ── Rounds ──
    roundsPerAg: Record<string, ScheduleConfigValue<number>>; // agId → rounds needed

    // ── Rounds per day (Calendar section state, persisted across sections) ──
    roundsPerDay: Record<string, number>;      // agegroupId → R/day

    // ── Agegroup ordering (derived from prior year source schedule) ──
    suggestedOrder?: string[];  // agegroupIds in suggested processing order

    // ── Division-level ordering (derived from per-division source timing) ──
    suggestedDivisionOrder?: string[];  // divisionIds in suggested processing order

    // ── League-specific rules (Node 5L) ──
    oddDivByeHandling?: ScheduleConfigValue<boolean>; // bPlayOddDivisionByeTeam
}

// ── Scope for scoped operations (used by ScheduleConfigService) ──

export type SchedulingScope = {
    level: 'event' | 'agegroup' | 'division';
    agegroupId?: string;
    divisionId?: string;
};

// ── localStorage schema ──

export interface SavedScheduleConfig extends ScheduleConfig {
    savedAt: string; // ISO timestamp
}

// ── Reset dialog options (used by schedule-config-panel + parent) ──

export interface DevResetOptions {
    games: boolean;
    strategyProfiles: boolean;
    pairings: boolean;
    /** Clear date/round assignments (TimeslotsLeagueSeasonDates) */
    dates: boolean;
    /** Clear field timeslot assignments (TimeslotsLeagueSeasonFields) */
    fieldTimeslots: boolean;
    /** When set, preconfigure (colors, dates, fields, pairings) from this source job after reset. */
    sourceJobId?: string;
}
