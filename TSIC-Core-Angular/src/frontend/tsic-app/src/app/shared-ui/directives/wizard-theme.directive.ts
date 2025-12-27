import { Directive, Input, OnChanges, Renderer2, ElementRef, SimpleChanges } from '@angular/core';

/**
 * Apply a wizard theme class to any host element.
 * Usage:
 *   <main [wizardTheme]="'family'"> ... </main>
 * Produces host class: wizard-theme-family
 */
@Directive({
    selector: '[wizardTheme]',
    standalone: true
})
export class WizardThemeDirective implements OnChanges {
    @Input('wizardTheme') theme: string | null = null;

    private currentClass: string | null = null;

    constructor(private readonly el: ElementRef, private readonly renderer: Renderer2) { }

    ngOnChanges(changes: SimpleChanges): void {
        if ('theme' in changes) {
            const t = this.theme?.trim();
            const next = t ? `wizard-theme-${t}` : null;
            if (this.currentClass && this.currentClass !== next) {
                this.renderer.removeClass(this.el.nativeElement, this.currentClass);
            }
            if (next) {
                this.renderer.addClass(this.el.nativeElement, next);
            }
            this.currentClass = next;
        }
    }
}
