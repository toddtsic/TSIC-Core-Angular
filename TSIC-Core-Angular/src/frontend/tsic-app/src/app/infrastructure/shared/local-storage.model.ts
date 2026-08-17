/**
 * Local Storage Keys
 * Centralized enum for all localStorage keys used in the application.
 * EVERY static key must be registered here — no bare string literals elsewhere.
 */
export enum LocalStorageKey {
    // ── Auth ──
    AuthToken = 'auth_token',
    RefreshToken = 'refresh_token',
    ClubRepClubCount = 'clubRepClubCount',

    // ── Navigation ──
    /**
     * Bumped to _v2 2026-08-16. The v1 writer parsed the first URL segment and screened it
     * with a six-name denylist, so `forgot-password`, `reset-password` and any mistyped URL
     * (the `**` wildcard keeps the typed path) were all stored as job paths. Those poisoned
     * values live in real browsers; the new key abandons them rather than trying to sniff
     * out which stored strings were bogus. LastLocationService purges the v1 key on start.
     *
     * That bump fixed the wrong-SEGMENT half only. The nonexistent-JOB half — a real
     * `:jobPath` binding for a job that does not exist — is closed by jobPathMatch (the
     * route no longer matches) plus a write gated on a confirmed job and a read that drops
     * the key when it no longer resolves. No third key bump should be needed.
     */
    LastJobPath = 'last_job_path_v2',

    // ── Theme / UI ──
    AppTheme = 'app-theme',
    AdminNavCollapsed = 'tsic.adminNavCollapsed',
    AdminNavLayout = 'tsic.adminNavLayout',
    /** Set once the user opens a search filters fly-in for the first time; retires the "Set Filters" discovery arrow. Shared across Search Registrations + Teams (identical pattern). */
    SearchFiltersDiscovered = 'tsic.searchFiltersDiscovered',

    // ── Scheduling ──
    SchedulePlacementMode = 'tsic.scheduleDivision.placementMode',
    AutoScheduleConfig = 'tsic.autoSchedule.config',
    ScheduleFilters = 'tsic.scheduleFilters',
}

/**
 * Type-safe localStorage value types.
 * Dynamic-key services (IdempotencyService, ThemeOverridesService) manage their
 * own key composition and are excluded from this schema.
 */
export interface LocalStorageSchema {
    [LocalStorageKey.AuthToken]: string;
    [LocalStorageKey.RefreshToken]: string;
    [LocalStorageKey.ClubRepClubCount]: string;
    [LocalStorageKey.LastJobPath]: string;
    [LocalStorageKey.AppTheme]: 'light' | 'dark';
    [LocalStorageKey.AdminNavCollapsed]: boolean;
    [LocalStorageKey.AdminNavLayout]: 'horizontal' | 'sidebar';
    [LocalStorageKey.SearchFiltersDiscovered]: boolean;
    [LocalStorageKey.SchedulePlacementMode]: 'mouse' | 'keyboard';
    [LocalStorageKey.AutoScheduleConfig]: string; // JSON-serialized AutoScheduleConfig
    [LocalStorageKey.ScheduleFilters]: string; // JSON-serialized ScheduleFiltersStoreV1
}
