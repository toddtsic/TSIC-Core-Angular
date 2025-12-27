/**
 * Local Storage Keys
 * Centralized enum for all localStorage keys used in the application
 */
export enum LocalStorageKey {
    /** Selected color palette index (0-7) */
    SelectedPalette = 'tsic-selected-palette',

    /** Authentication token */
    AuthToken = 'tsic_token',

    /** Theme overrides for job path (format: tsic:theme:{jobPath}:{theme}) */
    ThemeOverride = 'tsic:theme'
}

/**
 * User Preferences stored in localStorage
 */
export interface UserPreferences {
    /** Selected palette index (0-7) */
    selectedPaletteIndex?: number;
}

/**
 * Type-safe localStorage value types
 */
export interface LocalStorageSchema {
    [LocalStorageKey.SelectedPalette]: number;
    [LocalStorageKey.AuthToken]: string;
    [LocalStorageKey.ThemeOverride]: string; // JSON string
}
