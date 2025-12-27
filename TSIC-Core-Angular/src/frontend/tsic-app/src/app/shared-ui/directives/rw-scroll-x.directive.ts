import { Directive, ElementRef, HostListener, Input } from '@angular/core';

/**
 * rwScrollX: Convert wheel gestures to horizontal scroll and boost the amount.
 * Usage: <div [rwScrollX]="3"> ... </div>  // 3x multiplier (default 3)
 */
@Directive({
    selector: '[rwScrollX]',
    standalone: true
})
export class RwScrollXDirective {
    @Input('rwScrollX') multiplier = 3; // default 3x

    constructor(private readonly el: ElementRef<HTMLElement>) { }

    @HostListener('wheel', ['$event'])
    onWheel(evt: WheelEvent) {
        // Prefer the dominant axis (Y on mouse wheels; X on trackpads)
        const dominant = Math.abs(evt.deltaY) >= Math.abs(evt.deltaX) ? evt.deltaY : evt.deltaX;
        if (dominant === 0) return;
        const target = this.el.nativeElement;
        target.scrollLeft += dominant * this.multiplier;
        // Prevent default vertical scrolling while over the chips row
        evt.preventDefault();
    }
}
