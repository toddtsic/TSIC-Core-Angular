import { Injectable } from '@angular/core';
import { LocalStorageKey, LocalStorageSchema } from '../shared/local-storage.model';

/**
 * Type-safe wrapper for localStorage operations
 * Provides strongly-typed get/set methods with error handling
 */
@Injectable({
    providedIn: 'root'
})
export class LocalStorageService {
    /**
     * Get a typed value from localStorage
     * @param key The localStorage key
     * @param defaultValue Optional default value if key doesn't exist
     * @returns The stored value or default
     */
    get<K extends LocalStorageKey>(
        key: K,
        defaultValue?: LocalStorageSchema[K]
    ): LocalStorageSchema[K] | undefined {
        try {
            const value = localStorage.getItem(key);
            if (value === null) {
                return defaultValue;
            }

            // Try to parse as JSON for complex types
            try {
                return JSON.parse(value) as LocalStorageSchema[K];
            } catch {
                // Return as-is if not valid JSON (for string values)
                return value as LocalStorageSchema[K];
            }
        } catch (error) {
            console.warn(`Failed to read from localStorage [${key}]:`, error);
            return defaultValue;
        }
    }

    /**
     * Get a number from localStorage
     * @param key The localStorage key
     * @param defaultValue Default value if key doesn't exist or parsing fails
     * @returns The number value or default
     */
    getNumber(key: LocalStorageKey, defaultValue: number = 0): number {
        try {
            const value = localStorage.getItem(key);
            if (value === null) {
                return defaultValue;
            }

            const parsed = parseInt(value, 10);
            return isNaN(parsed) ? defaultValue : parsed;
        } catch (error) {
            console.warn(`Failed to read number from localStorage [${key}]:`, error);
            return defaultValue;
        }
    }

    /**
     * Get a string from localStorage
     * @param key The localStorage key
     * @param defaultValue Default value if key doesn't exist
     * @returns The string value or default
     */
    getString(key: LocalStorageKey, defaultValue: string = ''): string {
        try {
            return localStorage.getItem(key) ?? defaultValue;
        } catch (error) {
            console.warn(`Failed to read string from localStorage [${key}]:`, error);
            return defaultValue;
        }
    }

    /**
     * Set a typed value in localStorage
     * @param key The localStorage key
     * @param value The value to store
     */
    set<K extends LocalStorageKey>(key: K, value: LocalStorageSchema[K]): void {
        try {
            const serialized = typeof value === 'string' ? value : JSON.stringify(value);
            localStorage.setItem(key, serialized);
        } catch (error) {
            console.warn(`Failed to write to localStorage [${key}]:`, error);
        }
    }

    /**
     * Remove a key from localStorage
     * @param key The localStorage key to remove
     */
    remove(key: LocalStorageKey): void {
        try {
            localStorage.removeItem(key);
        } catch (error) {
            console.warn(`Failed to remove from localStorage [${key}]:`, error);
        }
    }

    /**
     * Check if a key exists in localStorage
     * @param key The localStorage key
     * @returns True if the key exists
     */
    has(key: LocalStorageKey): boolean {
        try {
            return localStorage.getItem(key) !== null;
        } catch (error) {
            console.warn(`Failed to check localStorage [${key}]:`, error);
            return false;
        }
    }

    /**
     * Clear all application localStorage (use with caution)
     */
    clear(): void {
        try {
            // Only clear keys that start with 'tsic'
            Object.values(LocalStorageKey).forEach(key => {
                if (key.startsWith('tsic')) {
                    localStorage.removeItem(key);
                }
            });
        } catch (error) {
            console.warn('Failed to clear localStorage:', error);
        }
    }
}
