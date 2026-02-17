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
    SelectedPalette = 'tsic-selected-palette',

    // ── Scheduling ──
    SchedulePlacementMode = 'tsic.scheduleDivision.placementMode',
}

/**
 * User Preferences stored in localStorage
 */
export interface UserPreferences {
    /** Selected palette index (0-7) */
    selectedPaletteIndex?: number;
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
    [LocalStorageKey.SelectedPalette]: number;
    [LocalStorageKey.SchedulePlacementMode]: 'mouse' | 'keyboard';
}
