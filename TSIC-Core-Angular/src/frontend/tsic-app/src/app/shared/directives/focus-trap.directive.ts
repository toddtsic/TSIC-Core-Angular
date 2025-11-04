import { Directive, ElementRef, HostListener, Input } from '@angular/core';

/**
 * Simple focus trap for native <dialog> and modal-like containers.
 * - Keeps Tab/Shift+Tab focus inside the host element when active.
 * - Does not steal initial focus; works alongside [autofocus].
 */
@Directive({
    selector: '[tsicFocusTrap]',
    standalone: true,
})
export class FocusTrapDirective {
    @Input('tsicFocusTrap') active = true;

    constructor(private readonly el: ElementRef<HTMLElement>) { }

    private getFocusableElements(): HTMLElement[] {
        const root = this.el.nativeElement;
        // Common focusable selectors
        const selectors = [
            'a[href]:not([tabindex="-1"]):not([disabled])',
            'button:not([tabindex="-1"]):not([disabled])',
            'input:not([type="hidden"]):not([tabindex="-1"]):not([disabled])',
            'select:not([tabindex="-1"]):not([disabled])',
            'textarea:not([tabindex="-1"]):not([disabled])',
            '[tabindex]:not([tabindex="-1"])',
        ];
        const nodes = Array.from(root.querySelectorAll<HTMLElement>(selectors.join(',')));
        // Only visible elements
        return nodes.filter((n) => !!(n.offsetWidth || n.offsetHeight || n.getClientRects().length));
    }

    @HostListener('keydown', ['$event'])
    onKeydown(event: KeyboardEvent) {
        if (!this.active) return;
        if (event.key !== 'Tab') return;

        const focusables = this.getFocusableElements();
        if (focusables.length === 0) return;

        const first = focusables[0];
        const last = focusables.at(-1)!;
        const current = document.activeElement as HTMLElement | null;

        if (event.shiftKey) {
            // Shift+Tab from first -> go to last
            if (!current || current === first) {
                last.focus();
                event.preventDefault();
            }
            return;
        }
        // Tab from last -> go to first
        if (!current || current === last) {
            first.focus();
            event.preventDefault();
        }
    }
}
