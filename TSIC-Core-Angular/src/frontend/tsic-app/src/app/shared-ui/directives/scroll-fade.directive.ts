import { AfterViewInit, Directive, ElementRef, OnDestroy, inject } from '@angular/core';

/**
 * appScrollFade — shows left/right edge fades on a horizontal scroller to signal
 * that more content exists off-screen.
 *
 * Apply to a WRAPPER element whose first child is the `overflow-x:auto` scroller.
 * The directive toggles `.scroll-fade--left` / `.scroll-fade--right` on the
 * wrapper based on scroll position; the fades themselves are CSS ::before/::after
 * on the wrapper (so they stay pinned to the visible edges, not the scrolled
 * content). Left fade shows once scrolled off the start; right fade hides at the
 * end. No effect() — plain scroll + ResizeObserver listeners.
 */
@Directive({
    selector: '[appScrollFade]',
    standalone: true
})
export class ScrollFadeDirective implements AfterViewInit, OnDestroy {
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private scroller: HTMLElement | null = null;
    private ro: ResizeObserver | null = null;
    private readonly onScroll = () => this.update();

    ngAfterViewInit(): void {
        this.scroller = this.host.nativeElement.firstElementChild as HTMLElement | null;
        if (!this.scroller) return;
        this.scroller.addEventListener('scroll', this.onScroll, { passive: true });
        if (typeof ResizeObserver !== 'undefined') {
            // Fires on initial observe + on viewport/size changes (e.g. rotate).
            this.ro = new ResizeObserver(() => this.update());
            this.ro.observe(this.scroller);
        }
        this.update();
    }

    ngOnDestroy(): void {
        this.scroller?.removeEventListener('scroll', this.onScroll);
        this.ro?.disconnect();
        this.ro = null;
    }

    private update(): void {
        const s = this.scroller;
        if (!s) return;
        const max = s.scrollWidth - s.clientWidth;
        const x = s.scrollLeft;
        const EPS = 1;
        const cl = this.host.nativeElement.classList;
        cl.toggle('scroll-fade--left', x > EPS);
        cl.toggle('scroll-fade--right', max > EPS && x < max - EPS);
    }
}
