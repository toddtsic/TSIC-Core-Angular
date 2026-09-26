import { AfterViewInit, Directive, ElementRef, OnDestroy, inject, input, output } from '@angular/core';

/** a swipe has to travel this far before it counts as one */
const SWIPE_MIN_DISTANCE = 60;
/** treat "within a pixel or two of the limit" as being at the limit */
const SWIPE_EDGE_SLOP = 2;
/** iOS Safari owns gestures starting this close to the left edge (back swipe) */
const SWIPE_EDGE_GUARD = 24;

/** the page swinging away, then its replacement swinging in */
const PAGE_OUT_MS = 240;
const PAGE_IN_MS = 280;
/** past 90 the page is edge-on and gone; a little further hides the seam */
const PAGE_ANGLE = 96;
/** viewing distance — lower exaggerates the 3D, higher flattens it */
const PAGE_PERSPECTIVE = 1400;
/** how dark the edge swinging away gets */
const PAGE_SHADE = 0.55;
/** the tug at the ends of the list */
const BOUNCE_MS = 220;
const BOUNCE_PX = 16;

/**
 * appSwipePager — swipe sideways to page through a list (age groups), with a
 * hinged page turn. Web port of the TSIC-Events mobile app's SwipePagerDirective
 * so the public schedule's Brackets and Standings tabs page the same way.
 *
 * The host is where the gesture is read and, by default, what turns;
 * `appSwipePagerTarget` splits the two where they differ.
 *
 * Paging only claims a gesture panning cannot use. At touchstart the directive
 * finds the nearest horizontally-scrollable element between the finger and the
 * host — on the bracket that is Syncfusion's own `overflow:auto` content div,
 * which it pans by setting scrollLeft — and pages only if that scroller is
 * already at its limit in the swipe's direction. With no scroller under the
 * finger (a plain list) every qualifying swipe pages.
 *
 * Touch events only: a mouse never pages, desktop keeps the pill strip.
 */
@Directive({
    selector: '[appSwipePager]',
    standalone: true
})
export class SwipePagerDirective implements AfterViewInit, OnDestroy {
    /** whether the list continues in each direction */
    readonly canPrev = input<boolean>(true, { alias: 'appSwipePagerCanPrev' });
    readonly canNext = input<boolean>(true, { alias: 'appSwipePagerCanNext' });
    /** the element that turns; defaults to the host */
    readonly target = input<HTMLElement | null>(null, { alias: 'appSwipePagerTarget' });

    /** -1 / +1, emitted mid-turn while the page is edge-on and invisible */
    readonly step = output<number>({ alias: 'appSwipePagerStep' });

    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    private shade?: HTMLElement;
    private paging = false;

    private readonly onTouchStart = (ev: TouchEvent) => this.touchStart(ev);
    private readonly onTouchEnd = (ev: TouchEvent) => this.touchEnd(ev);

    ngAfterViewInit(): void {
        // passive: nothing here calls preventDefault — panning and vertical
        // scrolling must stay native
        this.host.addEventListener('touchstart', this.onTouchStart, { passive: true });
        this.host.addEventListener('touchend', this.onTouchEnd, { passive: true });
        this.host.addEventListener('touchcancel', this.onTouchEnd, { passive: true });
    }

    ngOnDestroy(): void {
        this.host.removeEventListener('touchstart', this.onTouchStart);
        this.host.removeEventListener('touchend', this.onTouchEnd);
        this.host.removeEventListener('touchcancel', this.onTouchEnd);
        this.shade?.remove();
    }

    // ---- gesture ---------------------------------------------------------

    private tracking = false;
    private startX = 0;
    private startY = 0;
    private canGoPrev = false;
    private canGoNext = false;

    private touchStart(ev: TouchEvent): void {
        if (ev.touches.length !== 1) {
            this.tracking = false; // a second finger means pinch, never a swipe
            return;
        }
        const touch = ev.touches[0];
        if (touch.clientX < SWIPE_EDGE_GUARD) {
            this.tracking = false; // the browser's back gesture lives here
            return;
        }

        this.tracking = true;
        this.startX = touch.clientX;
        this.startY = touch.clientY;
        // room to pan is measured at the START: once the finger is down the
        // scroller consumes the movement until it hits the limit
        const scroller = this.scrollerUnder(ev.target);
        if (!scroller) {
            this.canGoPrev = this.canGoNext = true;
            return;
        }
        const maxScroll = scroller.scrollWidth - scroller.clientWidth;
        this.canGoPrev = scroller.scrollLeft <= SWIPE_EDGE_SLOP;
        this.canGoNext = scroller.scrollLeft >= maxScroll - SWIPE_EDGE_SLOP;
    }

    private touchEnd(ev: TouchEvent): void {
        const tracking = this.tracking;
        this.tracking = false;
        const touch = ev.changedTouches[0];
        if (!tracking || !touch || ev.touches.length > 0) return;

        const dx = touch.clientX - this.startX;
        const dy = touch.clientY - this.startY;
        // a deliberate, horizontal-dominant flick, or scrolling keeps tripping it
        if (Math.abs(dx) < SWIPE_MIN_DISTANCE || Math.abs(dx) < Math.abs(dy) * 1.5) return;

        // dragging RIGHT reveals what's to the left: the previous page
        if (dx > 0 && this.canGoPrev) {
            void (this.canPrev() ? this.turnPage(-1) : this.bounce(-1));
        } else if (dx < 0 && this.canGoNext) {
            void (this.canNext() ? this.turnPage(1) : this.bounce(1));
        }
    }

    /** Nearest element from the touch point up to the host that actually scrolls sideways. */
    private scrollerUnder(start: EventTarget | null): HTMLElement | null {
        let el = start instanceof Element ? start : null;
        while (el) {
            if (el instanceof HTMLElement && el.scrollWidth - el.clientWidth > SWIPE_EDGE_SLOP) {
                const ox = getComputedStyle(el).overflowX;
                if (ox === 'auto' || ox === 'scroll') return el;
            }
            if (el === this.host) break;
            el = el.parentElement;
        }
        return null;
    }

    // ---- the turn --------------------------------------------------------

    private turnTarget(): HTMLElement {
        return this.target() ?? this.host;
    }

    private reducedMotion(): boolean {
        return matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    /**
     * The page swings away about the edge it was flicked toward, the data changes
     * while it is edge-on and invisible, and its replacement swings in about the
     * OPPOSITE edge — the arrival side is what says which way you moved. The
     * perspective travels IN the transform, so no stacking context or scrolling
     * behaviour changes for the sake of an animation.
     */
    private async turnPage(delta: number): Promise<void> {
        if (this.paging) return;
        this.paging = true;

        // motion is the decoration; the change of page is not
        if (this.reducedMotion()) {
            this.step.emit(delta);
            this.paging = false;
            return;
        }

        const el = this.turnTarget();
        const rot = (deg: number) => `perspective(${PAGE_PERSPECTIVE}px) rotateY(${deg}deg)`;
        const leaveEdge = delta > 0 ? 'left center' : 'right center';
        const arriveEdge = delta > 0 ? 'right center' : 'left center';

        try {
            await this.leg(el, PAGE_OUT_MS, 'cubic-bezier(0.4, 0, 1, 1)', leaveEdge,
                [rot(0), rot(delta > 0 ? -PAGE_ANGLE : PAGE_ANGLE)], ['1', '0.45'], ['0', '1']);

            this.step.emit(delta);
            // felt, not seen — Android only; iOS Safari has no vibrate and that's fine
            try { navigator.vibrate?.(8); } catch { /* no haptic engine */ }

            await this.leg(el, PAGE_IN_MS, 'cubic-bezier(0, 0, 0.2, 1)', arriveEdge,
                [rot(delta > 0 ? PAGE_ANGLE : -PAGE_ANGLE), rot(0)], ['0.45', '1'], ['1', '0']);
        } finally {
            this.paging = false;
        }
    }

    /**
     * The end of the list. Turning a page to the same page reads as the app
     * losing the gesture, so it answers with a tug against the edge — what a
     * scroll view does at its limit — and no haptic, because the tick means
     * "you moved" and here you did not.
     */
    private async bounce(delta: number): Promise<void> {
        if (this.paging || this.reducedMotion()) return;
        this.paging = true;
        const px = delta > 0 ? -BOUNCE_PX : BOUNCE_PX;
        try {
            await this.turnTarget().animate(
                [
                    { offset: 0, transform: 'translateX(0)' },
                    { offset: 0.45, transform: `translateX(${px}px)` },
                    { offset: 1, transform: 'translateX(0)' }
                ],
                { duration: BOUNCE_MS, easing: 'ease-out' }
            ).finished;
        } catch {
            // cancelled (element torn down mid-tug) — nothing to restore
        } finally {
            this.paging = false;
        }
    }

    /**
     * One leg of the turn. The page and its shading share one duration and easing
     * and are awaited together, so the shadow never lags the page it belongs to.
     * No fill: when the leg ends the element is handed back with no leftover
     * transform to fight whatever else styles it.
     */
    private async leg(
        el: HTMLElement, duration: number, easing: string, hinge: string,
        transform: [string, string], opacity: [string, string], shadeOpacity: [string, string]
    ): Promise<void> {
        const timing: KeyframeAnimationOptions = { duration, easing, fill: 'forwards' };
        const page = el.animate(
            [
                { transformOrigin: hinge, transform: transform[0], opacity: opacity[0] },
                { transformOrigin: hinge, transform: transform[1], opacity: opacity[1] }
            ],
            timing
        );

        const shade = this.ensureShade(el);
        let shadeAnim: Animation | undefined;
        if (shade) {
            // darkest along the edge travelling AWAY from the viewer — the far edge from the hinge
            shade.style.background = `linear-gradient(${hinge === 'left center' ? 'to right' : 'to left'}, `
                + `rgba(0,0,0,0) 35%, rgba(0,0,0,${PAGE_SHADE}) 100%)`;
            shadeAnim = shade.animate(
                [{ opacity: shadeOpacity[0] }, { opacity: shadeOpacity[1] }],
                timing
            );
        }

        try {
            await Promise.all([page.finished, shadeAnim?.finished]);
        } catch {
            // cancelled — fall through and clear
        }
        // fill:'forwards' held the end frame until now so the page never flashes
        // back to rest between legs; drop it the moment the leg is done
        page.cancel();
        shadeAnim?.cancel();
        if (shade) shade.style.background = '';
    }

    /**
     * The light on the turning page, created here rather than asked of every
     * caller's markup: a surface rotating at constant brightness gives the eye no
     * depth cue. Needs a positioned target to cover; if the target is static it
     * skips the shading rather than change the caller's layout underneath them.
     */
    private ensureShade(el: HTMLElement): HTMLElement | undefined {
        if (this.shade?.isConnected && this.shade.parentElement === el) return this.shade;
        if (getComputedStyle(el).position === 'static') return undefined;
        const shade = document.createElement('div');
        shade.setAttribute('aria-hidden', 'true');
        shade.style.cssText = 'position:absolute;inset:0;opacity:0;pointer-events:none;z-index:1;';
        el.appendChild(shade);
        this.shade = shade;
        return shade;
    }
}
