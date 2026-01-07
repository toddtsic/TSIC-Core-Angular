import { Injectable } from '@angular/core';

/**
 * User preferences stored in localStorage
 */
export interface UserPreferences {
    teamLibraryInfoRead?: boolean;
    clubRepModalInfoRead?: boolean;
    // Add more preferences as needed
}

/**
 * Service for managing user preferences in localStorage with strong typing
 */
@Injectable({
    providedIn: 'root'
})
export class UserPreferencesService {
    private readonly STORAGE_KEY = 'tsic_user_preferences';

    /**
     * Get all user preferences
     */
    getPreferences(): UserPreferences {
        try {
            const stored = localStorage.getItem(this.STORAGE_KEY);
            return stored ? JSON.parse(stored) : {};
        } catch (error) {
            console.error('Error reading user preferences:', error);
            return {};
        }
    }

    /**
     * Save all user preferences
     */
    private savePreferences(preferences: UserPreferences): void {
        try {
            localStorage.setItem(this.STORAGE_KEY, JSON.stringify(preferences));
        } catch (error) {
            console.error('Error saving user preferences:', error);
        }
    }

    /**
     * Get a specific preference value
     */
    getPreference<K extends keyof UserPreferences>(key: K): UserPreferences[K] {
        const prefs = this.getPreferences();
        return prefs[key];
    }

    /**
     * Set a specific preference value
     */
    setPreference<K extends keyof UserPreferences>(key: K, value: UserPreferences[K]): void {
        const prefs = this.getPreferences();
        prefs[key] = value;
        this.savePreferences(prefs);
    }

    /**
     * Check if team library info has been read
     */
    isTeamLibraryInfoRead(): boolean {
        return this.getPreference('teamLibraryInfoRead') === true;
    }

    /**
     * Mark team library info as read
     */
    markTeamLibraryInfoAsRead(): void {
        this.setPreference('teamLibraryInfoRead', true);
    }

    /**
     * Check if club rep modal info has been read
     */
    isClubRepModalInfoRead(): boolean {
        return this.getPreference('clubRepModalInfoRead') === true;
    }

    /**
     * Mark club rep modal info as read
     */
    markClubRepModalInfoAsRead(): void {
        this.setPreference('clubRepModalInfoRead', true);
    }
}
