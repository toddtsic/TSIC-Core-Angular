import { Injectable, inject, signal } from '@angular/core';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { LocalStorageService } from '@infrastructure/services/local-storage.service';

/**
 * Shared state service for layout coordination
 * Allows header, menu, and dashboard components to coordinate without tight coupling
 */
@Injectable({
    providedIn: 'root'
})
export class MenuStateService {
    private readonly localStorage = inject(LocalStorageService);

    /** Mobile offcanvas open/closed state */
    offcanvasOpen = signal(false);

    /**
     * Desktop admin sidebar collapsed (icon-rail) vs expanded (labels) state.
     * Persisted to localStorage; default = collapsed (icon rail) for first-time
     * admins — maximises content width, labels are one click away. Only an explicit
     * stored 'false' expands it. Write-through in toggleSidebar() — no effect().
     */
    sidebarCollapsed = signal<boolean>(
        this.localStorage.get(LocalStorageKey.AdminNavCollapsed, true) ?? true
    );

    /** Toggle the desktop admin sidebar collapse state and persist the choice */
    toggleSidebar(): void {
        const collapsed = !this.sidebarCollapsed();
        this.sidebarCollapsed.set(collapsed);
        this.localStorage.set(LocalStorageKey.AdminNavCollapsed, collapsed);
    }

    /**
     * Desktop admin nav layout: 'sidebar' (vertical left rail) vs 'horizontal'
     * (the familiar top pill bar). Persisted; default = sidebar. Write-through (no effect()).
     */
    navLayout = signal<'horizontal' | 'sidebar'>(
        this.localStorage.get(LocalStorageKey.AdminNavLayout) === 'horizontal' ? 'horizontal' : 'sidebar'
    );

    /** Flip between sidebar and horizontal admin nav and persist the choice */
    toggleNavLayout(): void {
        const layout = this.navLayout() === 'sidebar' ? 'horizontal' : 'sidebar';
        this.navLayout.set(layout);
        this.localStorage.set(LocalStorageKey.AdminNavLayout, layout);
    }

    /** Fires when user requests dashboard customization (from header dropdown) */
    customizeDashboardRequested = signal(false);

    /** Toggle offcanvas sidebar */
    toggleOffcanvas(): void {
        this.offcanvasOpen.update(open => !open);
    }

    /**
     * Which top-level category is expanded inside the full-menu offcanvas accordion
     * (the header hamburger). Single-open. Toggled by tapping a parent in the offcanvas.
     */
    offcanvasExpandedId = signal<string | null>(null);

    /** Toggle a category open/closed inside the offcanvas (single-open accordion) */
    toggleOffcanvasCategory(navItemId: string | number): void {
        const id = String(navItemId);
        this.offcanvasExpandedId.update(cur => (cur === id ? null : id));
    }

    /** Close offcanvas */
    closeOffcanvas(): void {
        this.offcanvasOpen.set(false);
    }

    /**
     * Mobile focused-sheet: tapping a category tab in the bottom nav opens a
     * full-width sheet showing ONLY that category's children (rises above the
     * still-visible tab bar). Holds the open category's nav-item id, or null.
     */
    mobileSheetCategoryId = signal<string | null>(null);

    /** Toggle the focused sheet for a category (re-tapping the same tab closes it). */
    toggleMobileSheet(navItemId: string | number): void {
        const id = String(navItemId);
        this.offcanvasOpen.set(false); // never overlap with the full-menu offcanvas
        this.mobileSheetCategoryId.update(cur => (cur === id ? null : id));
    }

    /** Close the focused sheet. */
    closeMobileSheet(): void {
        this.mobileSheetCategoryId.set(null);
    }

    /** Pulse: requests all open menus/dropdowns to close (header dropdown, mobile menu, offcanvas) */
    closeAllMenusRequested = signal(false);

    requestCloseAllMenus(): void {
        this.closeAllMenusRequested.set(false); // reset first so re-trigger works
        this.closeAllMenusRequested.set(true);
    }

    ackCloseAllMenus(): void {
        this.closeAllMenusRequested.set(false);
    }

    /** Request the dashboard to open its customize dialog */
    requestCustomizeDashboard(): void {
        // Pulse: set true, then reset so it can be triggered again
        this.customizeDashboardRequested.set(true);
    }

    /** Acknowledge the request (called by the dashboard after opening) */
    ackCustomizeDashboard(): void {
        this.customizeDashboardRequested.set(false);
    }
}
