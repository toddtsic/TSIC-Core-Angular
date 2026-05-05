/**
 * Frontend-only persistence shape for the schedule page's filter state.
 * Lives outside the @core/api regen pipeline because it never crosses the wire —
 * it's pure browser-local state.
 *
 * Persisted under LocalStorageKey.ScheduleFilters as a JSON-serialized
 * ScheduleFiltersStoreV1. The schemaVersion tag exists so future readers can
 * detect and discard incompatible shapes instead of crashing on unexpected fields.
 *
 * Scope is per-tournament (keyed by jobPath, the URL slug used by the public
 * schedule route). A returning visitor sees only the filters they set on this
 * specific tournament — exactly the behavior the user requested.
 */

export const SCHEDULE_FILTERS_SCHEMA_VERSION = 1 as const;

export interface ScheduleFiltersStoreV1 {
    readonly schemaVersion: typeof SCHEDULE_FILTERS_SCHEMA_VERSION;
    readonly tournamentsByJobPath: Readonly<Record<string, TournamentFilterState>>;
}

export interface TournamentFilterState {
    /** Direct team selections (from the typeahead multiselect or row stars). */
    readonly teamIds: readonly string[];

    /** Simple-filter state — session would otherwise drop these. */
    readonly selectedGameDay: string;
    readonly selectedTime: string;
    readonly selectedFieldId: string;
    readonly unscoredOnly: boolean;

    /**
     * Tracks whether the one-time family-roster auto-seed has run for this
     * tournament. Once true, the seed never runs again — localStorage is
     * authoritative even if the user has cleared their selections.
     */
    readonly seededFromFamily: boolean;
    readonly updatedAt: string; // ISO 8601
}

export function emptyTournamentState(): TournamentFilterState {
    return {
        teamIds: [],
        selectedGameDay: '',
        selectedTime: '',
        selectedFieldId: '',
        unscoredOnly: false,
        seededFromFamily: false,
        updatedAt: new Date().toISOString(),
    };
}
