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
    LastJobPath = 'last_job_path',

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
