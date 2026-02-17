import { Injectable, signal } from '@angular/core';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';

@Injectable({
    providedIn: 'root'
})
export class ThemeService {

    // Signal for reactive theme state
    theme = signal<'light' | 'dark'>(this.getInitialTheme());

    constructor() {
        // Apply initial theme
        this.applyTheme(this.theme());
    }

    private getInitialTheme(): 'light' | 'dark' {
        // Check localStorage first
        const stored = localStorage.getItem(LocalStorageKey.AppTheme);
        if (stored === 'light' || stored === 'dark') {
            return stored;
        }

        // Fall back to system preference
        if (globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }

        return 'light';
    }

    toggleTheme(): void {
        const newTheme = this.theme() === 'light' ? 'dark' : 'light';
        this.setTheme(newTheme);
    }

    setTheme(theme: 'light' | 'dark'): void {
        this.theme.set(theme);
        this.applyTheme(theme);
        localStorage.setItem(LocalStorageKey.AppTheme, theme);
    }

    private applyTheme(theme: 'light' | 'dark'): void {
        document.documentElement.dataset['bsTheme'] = theme;

        // Apply our custom theme class to document root
        if (theme === 'dark') {
            document.documentElement.classList.add('theme-dark');
        } else {
            document.documentElement.classList.remove('theme-dark');
        }

        // Apply Syncfusion dark mode class for their components
        if (theme === 'dark') {
            document.body.classList.add('e-dark-mode');
        } else {
            document.body.classList.remove('e-dark-mode');
        }
    }
}
