import { AfterViewInit, Directive, ElementRef, OnDestroy, Renderer2, inject, input } from '@angular/core';

/**
 * appResizablePanel — makes ANY fly-in / slide-out panel width-resizable via a
 * drag handle on its inner edge, direction-aware for right- and left-anchored
 * panels. Drop it onto the panel element (whatever its class name) — the handle
 * is created and wired by the directive, so consumers add zero markup.
 *
 * Usage:
 *   <div class="detail-panel" appResizablePanel [storageKey]="'regDetailPanelWidth'"></div>
 *   <aside class="filters-panel" appResizablePanel storageKey="regFiltersWidth" panelSide="left"></aside>
 *
 *  - `panelSide="right"` (default): panel anchored to the right → handle on the
 *    LEFT inner edge; dragging left widens it.
 *  - `panelSide="left"`: panel anchored to the left → handle on the RIGHT inner
 *    edge; dragging right widens it.
 *
 * Width persists per-panel under `storageKey` in localStorage. Double-click the
 * handle to reset to `defaultWidth`. Pointer capture routes move/up back to the
 * handle — no document listeners, no effect(); width is applied imperatively.
 *
 * The handle styling lives in styles/_flyin.scss (`.resize-handle`), and a global
 * `@media (max-width: 767.98px) { [appResizablePanel] { width: 100vw !important } }`
 * keeps every panel full-bleed on mobile despite a stored desktop width.
 */
@Directive({
    selector: '[appResizablePanel]',
    standalone: true
})
export class ResizablePanelDirective implements AfterViewInit, OnDestroy {
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly renderer = inject(Renderer2);

    storageKey = input.required<string>();
    panelSide = input<'left' | 'right'>('right');
    defaultWidth = input<number>(560);
    minWidth = input<number>(480);
    maxWidth = input<number>(1100);

    private width = 0;
    private resizing = false;
    private resizeStartX = 0;
    private resizeStartWidth = 0;
    private readonly unlisten: (() => void)[] = [];

    ngAfterViewInit(): void {
        this.width = this.readStoredWidth();
        this.applyWidth();

        const handle = this.renderer.createElement('div') as HTMLElement;
        this.renderer.addClass(handle, 'resize-handle');
        // Right-anchored panel → handle on the left inner edge, and vice versa.
        this.renderer.addClass(handle, this.panelSide() === 'left' ? 'resize-handle--right' : 'resize-handle--left');
        this.renderer.setAttribute(handle, 'role', 'separator');
        this.renderer.setAttribute(handle, 'aria-orientation', 'vertical');
        this.renderer.setAttribute(handle, 'aria-label', 'Resize panel');
        this.renderer.setAttribute(handle, 'title', 'Drag to resize · double-click to reset');
        this.renderer.appendChild(this.host.nativeElement, handle);

        this.unlisten.push(
            this.renderer.listen(handle, 'pointerdown', (ev: PointerEvent) => this.startResize(ev)),
            this.renderer.listen(handle, 'pointermove', (ev: PointerEvent) => this.onResizeMove(ev)),
            this.renderer.listen(handle, 'pointerup', (ev: PointerEvent) => this.endResize(ev)),
            this.renderer.listen(handle, 'dblclick', () => this.resetWidth())
        );
    }

    ngOnDestroy(): void {
        this.unlisten.forEach(fn => fn());
        this.unlisten.length = 0;
    }

    private startResize(ev: PointerEvent): void {
        ev.preventDefault();
        this.resizeStartX = ev.clientX;
        this.resizeStartWidth = this.width;
        this.resizing = true;
        this.renderer.addClass(this.host.nativeElement, 'resizing');
        (ev.target as HTMLElement).setPointerCapture?.(ev.pointerId);
    }

    private onResizeMove(ev: PointerEvent): void {
        if (!this.resizing) return;
        // Right-anchored grows as the pointer moves left; left-anchored is inverted.
        const dir = this.panelSide() === 'left' ? -1 : 1;
        const delta = (this.resizeStartX - ev.clientX) * dir;
        this.width = this.clampWidth(this.resizeStartWidth + delta);
        this.applyWidth();
    }

    private endResize(ev: PointerEvent): void {
        if (!this.resizing) return;
        this.resizing = false;
        this.renderer.removeClass(this.host.nativeElement, 'resizing');
        (ev.target as HTMLElement).releasePointerCapture?.(ev.pointerId);
        try { localStorage.setItem(this.storageKey(), String(this.width)); } catch { /* ignore */ }
    }

    private resetWidth(): void {
        this.width = this.defaultWidth();
        this.applyWidth();
        try { localStorage.removeItem(this.storageKey()); } catch { /* ignore */ }
    }

    private applyWidth(): void {
        this.renderer.setStyle(this.host.nativeElement, 'width', `${this.width}px`);
    }

    private readStoredWidth(): number {
        try {
            const raw = Number(localStorage.getItem(this.storageKey()));
            if (raw && !Number.isNaN(raw)) return this.clampWidth(raw);
        } catch { /* localStorage unavailable — fall through to default */ }
        return this.defaultWidth();
    }

    private clampWidth(w: number): number {
        const max = Math.min(this.maxWidth(), Math.round(window.innerWidth * 0.9));
        return Math.max(this.minWidth(), Math.min(max, w));
    }
}
