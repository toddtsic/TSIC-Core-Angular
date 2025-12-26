import { Injectable, signal } from '@angular/core';

/**
 * Shared state service for mobile menu offcanvas
 * Allows header and menu components to coordinate without tight coupling
 */
@Injectable({
    providedIn: 'root'
})
export class MenuStateService {
    /** Mobile offcanvas open/closed state */
    offcanvasOpen = signal(false);

    /** Toggle offcanvas sidebar */
    toggleOffcanvas(): void {
        this.offcanvasOpen.update(open => !open);
    }

    /** Close offcanvas */
    closeOffcanvas(): void {
        this.offcanvasOpen.set(false);
    }
}
