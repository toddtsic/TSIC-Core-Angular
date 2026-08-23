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
     * That bump fixed the wrong-SEGMENT half only — it still could not tell whether the job
     * EXISTS. That half is closed by moving the write off NavigationEnd and onto resolved job
     * metadata: LastLocationService now stores a jobPath only once the server has returned it,
     * and drops it when that job 404s. No third key bump should be needed.
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

/**
 * Fly-in / slide-out panel widths persisted by `appResizablePanel`.
 *
 * Deliberately a literal union rather than `LocalStorageKey` members: the directive takes its
 * key as a template attribute, and a union narrows a static string at compile time without
 * every host component having to import the enum. Adding a panel means adding its key HERE —
 * an unregistered string is a build error, which is the guarantee the enum exists to give.
 *
 * Known exclusion, same as the dynamic-key services noted above: these are NOT swept by
 * `LocalStorageService.clear()`, which iterates `LocalStorageKey`. Panel widths are disposable
 * per-device UI state, so that is acceptable — but it is a decision, not an oversight.
 *
 * `-v2` on the two detail panels (2026-08-23, AR-016): those panels gained a percentage-based
 * opening width, and a stored width beats any default. Every existing user — Ann included, who
 * asked for the change — would otherwise have kept the old 560px forever. Bumping the key drops
 * everyone onto the new default exactly once; later drags persist normally. Same technique as
 * `LastJobPath` above.
 */
export type PanelWidthKey =
    | 'regDetailPanelWidth-v2'
    | 'teamDetailPanelWidth-v2'
    | 'clubRostersPanelWidth'
    | 'ladtPanelWidth'
    | 'ladtTreeWidth'
    | 'regFiltersWidth'
    | 'teamFiltersWidth'
    | 'helpPanelWidth'
    | 'libraryPanelWidth'
    | 'scoreEntryPanelWidth'
    | 'teamResultsPanelWidth';
