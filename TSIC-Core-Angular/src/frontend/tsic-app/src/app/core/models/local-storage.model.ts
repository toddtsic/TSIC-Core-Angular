/**
 * Local Storage Keys
 * Centralized enum for all localStorage keys used in the application
 */
export enum LocalStorageKey {
    /** Selected color palette index (0-7) */
    SelectedPalette = 'tsic-selected-palette',

    /** Last username entered on login */
    LastUsername = 'last_username',

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

    /** Last username used for login */
    lastUsername?: string;
}

/**
 * Type-safe localStorage value types
 */
export interface LocalStorageSchema {
    [LocalStorageKey.SelectedPalette]: number;
    [LocalStorageKey.LastUsername]: string;
    [LocalStorageKey.AuthToken]: string;
    [LocalStorageKey.ThemeOverride]: string; // JSON string
}
