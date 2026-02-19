import { Injectable, signal } from '@angular/core';

/**
 * Shared state service for layout coordination
 * Allows header, menu, and dashboard components to coordinate without tight coupling
 */
@Injectable({
    providedIn: 'root'
})
export class MenuStateService {
    /** Mobile offcanvas open/closed state */
    offcanvasOpen = signal(false);

    /** Fires when user requests dashboard customization (from header dropdown) */
    customizeDashboardRequested = signal(false);

    /** Toggle offcanvas sidebar */
    toggleOffcanvas(): void {
        this.offcanvasOpen.update(open => !open);
    }

    /** Close offcanvas */
    closeOffcanvas(): void {
        this.offcanvasOpen.set(false);
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
