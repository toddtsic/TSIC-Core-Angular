import { AfterViewInit, Directive, ElementRef, OnDestroy, Renderer2, booleanAttribute, inject, input } from '@angular/core';

/**
 * appDraggableModal — makes a hand-rolled div modal (the `.modal-overlay` >
 * `.modal-card` pattern) movable by its header, the same way TsicDialogComponent
 * bakes drag into native `<dialog>` modals. Drop it on the `.modal-card`:
 *
 *   <div class="modal-card" appDraggableModal> … </div>
 *
 * The handle is resolved (in order) as `[data-tsic-drag-handle]`, `.modal-header`,
 * or the card's first child (covers hero-style headers). Interactive controls in
 * the header (buttons, inputs, links) never start a drag. Movement is a plain
 * `transform: translate()` on the card, clamped to the viewport. Because the
 * modal is re-created each time it opens (it lives inside an `@if`), position
 * naturally resets to centered on every reopen — no open/close wiring needed.
 *
 * Opt out with `[appDraggableModal]="false"` (e.g. to pin a specific modal).
 */
@Directive({
    selector: '[appDraggableModal]',
    standalone: true
})
export class DraggableModalDirective implements AfterViewInit, OnDestroy {
    /** Whether the modal can be repositioned by dragging its header. Default true.
     *  booleanAttribute lets a bare `appDraggableModal` attribute mean true while
     *  still honouring `[appDraggableModal]="false"` to pin a modal in place. */
    readonly enabled = input(true, { alias: 'appDraggableModal', transform: booleanAttribute });

    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly renderer = inject(Renderer2);

    private offsetX = 0;
    private offsetY = 0;
    private dragging = false;
    private pointerId: number | null = null;
    private startClientX = 0;
    private startClientY = 0;
    private baseX = 0;
    private baseY = 0;
    /** Untransformed geometry captured at drag start, for viewport clamping. */
    private naturalLeft = 0;
    private naturalTop = 0;
    private cardWidth = 0;
    private cardHeight = 0;
    /** Lives for the directive's lifetime (the pointerdown listener). */
    private readonly disposers: Array<() => void> = [];
    /** Lives only for a single drag gesture (move/up/cancel); cleared on release. */
    private dragSessionDisposers: Array<() => void> = [];

    ngAfterViewInit(): void {
        if (!this.enabled()) return;
        const card = this.host.nativeElement;
        this.disposers.push(
            this.renderer.listen(card, 'pointerdown', (e: PointerEvent) => this.onDragPointerDown(e)),
        );
        this.markDragHandle();
    }

    ngOnDestroy(): void {
        this.endDragSession();
        for (const dispose of this.disposers) dispose();
        this.disposers.length = 0;
    }

    private endDragSession(): void {
        for (const dispose of this.dragSessionDisposers) dispose();
        this.dragSessionDisposers = [];
    }

    /** Resolve the header handle within the modal card. */
    private resolveHandle(): HTMLElement | null {
        const card = this.host.nativeElement;
        return (
            card.querySelector<HTMLElement>('[data-tsic-drag-handle]') ??
            card.querySelector<HTMLElement>('.modal-header') ??
            (card.firstElementChild as HTMLElement | null)
        );
    }

    /** Tag the resolved handle and give it the grab cursor. Content may not be laid
     *  out on the exact frame we init, so defer one frame. */
    private markDragHandle(): void {
        requestAnimationFrame(() => {
            const handle = this.resolveHandle();
            if (handle) {
                this.renderer.setAttribute(handle, 'data-tsic-drag-handle', 'true');
                this.renderer.setStyle(handle, 'cursor', 'grab');
            }
        });
    }

    private onDragPointerDown(e: PointerEvent): void {
        if (e.button !== 0) return;
        const handle = this.resolveHandle();
        if (!handle) return;

        const target = e.target as HTMLElement | null;
        if (!target || !handle.contains(target)) return;
        // Never hijack interactive controls that live in the header.
        if (target.closest('button, a, input, select, textarea, label, [contenteditable="true"]')) return;

        const card = this.host.nativeElement;
        const rect = card.getBoundingClientRect();
        this.naturalLeft = rect.left - this.offsetX;
        this.naturalTop = rect.top - this.offsetY;
        this.cardWidth = rect.width;
        this.cardHeight = rect.height;

        this.dragging = true;
        this.pointerId = e.pointerId;
        this.startClientX = e.clientX;
        this.startClientY = e.clientY;
        this.baseX = this.offsetX;
        this.baseY = this.offsetY;

        try { handle.setPointerCapture(e.pointerId); } catch { /* no-op */ }
        this.renderer.addClass(card, 'is-dragging');
        this.renderer.setStyle(handle, 'cursor', 'grabbing');

        this.dragSessionDisposers.push(
            this.renderer.listen(handle, 'pointermove', (ev: PointerEvent) => this.onDragPointerMove(ev)),
            this.renderer.listen(handle, 'pointerup', (ev: PointerEvent) => this.onDragPointerUp(ev)),
            this.renderer.listen(handle, 'pointercancel', (ev: PointerEvent) => this.onDragPointerUp(ev)),
        );
        e.preventDefault();
    }

    private onDragPointerMove(e: PointerEvent): void {
        if (!this.dragging || e.pointerId !== this.pointerId) return;

        let nx = this.baseX + (e.clientX - this.startClientX);
        let ny = this.baseY + (e.clientY - this.startClientY);

        // Clamp so the card stays within the viewport. If the card is larger than
        // the viewport on an axis, the min/max invert — Math.min/max on the bounds
        // keeps the range valid either way.
        const minX = -this.naturalLeft;
        const maxX = window.innerWidth - this.naturalLeft - this.cardWidth;
        const minY = -this.naturalTop;
        const maxY = window.innerHeight - this.naturalTop - this.cardHeight;
        nx = Math.min(Math.max(nx, Math.min(minX, maxX)), Math.max(minX, maxX));
        ny = Math.min(Math.max(ny, Math.min(minY, maxY)), Math.max(minY, maxY));

        this.offsetX = nx;
        this.offsetY = ny;
        this.renderer.setStyle(this.host.nativeElement, 'transform', `translate(${nx}px, ${ny}px)`);
    }

    private onDragPointerUp(e: PointerEvent): void {
        if (e.pointerId !== this.pointerId) return;
        this.dragging = false;
        this.pointerId = null;
        this.renderer.removeClass(this.host.nativeElement, 'is-dragging');
        const handle = e.currentTarget as HTMLElement | null;
        if (handle) this.renderer.setStyle(handle, 'cursor', 'grab');
        this.endDragSession();
    }
}
