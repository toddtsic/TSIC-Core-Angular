import { Directive, ElementRef, AfterViewInit } from '@angular/core';

/**
 * Autofocus directive that focuses an input element after it's rendered in the DOM.
 * Particularly useful for modal inputs that are conditionally rendered.
 * 
 * Usage:
 * ```html
 * <input type="text" appAutofocus placeholder="Will be focused automatically">
 * ```
 */
@Directive({
    selector: '[appAutofocus]',
    standalone: true
})
export class AutofocusDirective implements AfterViewInit {
    constructor(private el: ElementRef) { }

    ngAfterViewInit(): void {
        // Use setTimeout to ensure the element is fully rendered and ready for focus
        setTimeout(() => {
            this.el.nativeElement.focus();
        }, 0);
    }
}
