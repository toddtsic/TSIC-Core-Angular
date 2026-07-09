import { ChangeDetectionStrategy, Component, ElementRef, AfterViewInit, OnChanges, OnDestroy, Renderer2, SimpleChanges, inject, input, output, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FocusTrapDirective } from '../../directives/focus-trap.directive';

/**
 * Reusable wrapper for native <dialog> with an integrated focus trap and ESC-to-close.
 * Usage:
 *  <tsic-dialog [open]="true" size="lg" (requestClose)="onClose()">
 *    <div class="modal-content"> ... </div>
 *  </tsic-dialog>
 *
 * Dragging: modals are movable by default — grab the header and drag. The drag
 * handle is resolved (in order) as `[data-tsic-drag-handle]`, `.modal-header`,
 * or the first child of `.modal-content` (covers hero-style headers). Interactive
 * controls in the header (buttons, inputs, links) never start a drag. Pass
 * `[draggable]="false"` to pin a dialog in place.
 */
@Component({
    selector: 'tsic-dialog',
    standalone: true,
    imports: [CommonModule, FocusTrapDirective],
    template: `
    <dialog
      #dlg
      class="tsic-dialog"
      [ngClass]="sizeClass"
      (keydown.escape)="onEsc()"
      (cancel)="$event.preventDefault()"
      (mousedown)="onBackdropMousedown($event)"
      (click)="onBackdropClick($event)"
      [tsicFocusTrap]="true"
      aria-modal="true"
    >
      <ng-content></ng-content>
    </dialog>
  `,
    styles: [
        `
                /* Position and container */
                .tsic-dialog {
                    border: none;
                    border-radius: 0.5rem;
                    padding: 0;
                    /* Let UA/top-layer handle centering to avoid stacking quirks */
                    inset: 0; 
                    margin: auto;
                    width: min(720px, 96vw);
                    max-height: 90vh;
                    box-shadow: var(--shadow-lg, 0 0.75rem 1.5rem rgba(0,0,0,.25));
                    background: transparent; /* content provides background */
                }
                .tsic-dialog.tsic-dialog-sm { width: min(480px, 96vw); }
                .tsic-dialog.tsic-dialog-lg { width: min(960px, 96vw); }

                        /* Content chrome - use deep selector so projected content is styled */
                        :host ::ng-deep .modal-content {
                    border: 1px solid var(--border-color, rgba(0,0,0,.1));
                    border-radius: var(--radius-md, 0.5rem);
                    background: var(--bs-body-bg, #fff);
                    color: var(--bs-body-color, inherit);
                    max-height: 90vh;
                    overflow: auto;
                    animation: tsicDialogContentIn .14s ease-out;
                    transform-origin: center;
                }
                        :host ::ng-deep .modal-header,
                        :host ::ng-deep .modal-body,
                        :host ::ng-deep .modal-footer {
                    padding: var(--space-4, 1rem);
                    background: var(--bs-body-bg, #fff);
                }
                        :host ::ng-deep .modal-header {
                    display: flex; align-items: center; justify-content: space-between;
                    border-bottom: 2px solid var(--bs-primary, rgba(0,0,0,.1));
                    padding: var(--space-3, 0.75rem) var(--space-4, 1rem);
                }
                        :host ::ng-deep .modal-footer {
                    display: flex; justify-content: flex-end; gap: .5rem;
                    border-top: 1px solid var(--border-color, rgba(0,0,0,.1));
                    background: var(--brand-bg-secondary, var(--bs-body-bg, #fff));
                }
                        :host ::ng-deep .modal-title {
                    margin: 0;
                    font-size: var(--font-size-lg, 1.1rem);
                    font-weight: var(--font-weight-semibold, 600);
                }
                        :host ::ng-deep .modal-title i { color: var(--bs-primary); }
                        :host ::ng-deep .btn-close { box-shadow: none !important; outline: none !important; }

                /* Backdrop */
                .tsic-dialog::backdrop {
                    background: rgba(0,0,0,.5);
                    animation: tsicDialogFadeIn .12s ease-out;
                }
                @keyframes tsicDialogFadeIn { from { opacity: 0; } to { opacity: 1; } }
                @keyframes tsicDialogContentIn { from { opacity: 0; transform: scale(.98); } to { opacity: 1; transform: scale(1); } }

                /* Suppress text selection while a drag is in flight. The grab/grabbing
                   cursor is set inline on the resolved handle from TS (the handle lives
                   in projected content, so ::ng-deep would be the only CSS route — inline
                   styling avoids piercing encapsulation). */
                .tsic-dialog.is-dragging { user-select: none; }
                `
    ],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicDialogComponent implements AfterViewInit, OnChanges, OnDestroy {
    /** Controls the native <dialog> open state. Often left as true when wrapped in an @if block. */
    readonly open = input(true);
    /** Size variant to add modifier class (e.g., tsic-dialog-lg). */
    readonly size = input<'sm' | 'md' | 'lg' | ''>('');
    /** Whether pressing ESC should emit requestClose (default true). */
    readonly closeOnEsc = input(true);
    /** Close the dialog when clicking on the backdrop area (outside content). Default true. */
    readonly closeOnBackdrop = input(true);
    /** Whether the modal can be repositioned by dragging its header. Default true. */
    readonly draggable = input(true);

    readonly requestClose = output<void>();

    readonly dialogEl = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

    private readonly renderer = inject(Renderer2);

    get sizeClass() {
        return {
            'tsic-dialog-sm': this.size() === 'sm',
            'tsic-dialog-lg': this.size() === 'lg',
        };
    }

    onEsc() {
        if (this.closeOnEsc()) {
            // TODO: The 'emit' function requires a mandatory void argument
            this.requestClose.emit();
        }
    }

    /** Track where mousedown started so drag-from-content doesn't close the dialog */
    private mousedownTarget: EventTarget | null = null;

    onBackdropMousedown(event: MouseEvent) {
        this.mousedownTarget = event.target;
    }

    onBackdropClick(event: MouseEvent) {
        if (!this.closeOnBackdrop()) return;
        const dialog = this.dialogEl()?.nativeElement;
        if (!dialog) return;
        // Only close if BOTH mousedown and click landed on the <dialog> itself (backdrop area).
        // This prevents closing when the user clicks inside an input and the mouseup
        // drifts onto the dialog element (common with text selection or slight drags).
        if (event.target === dialog && this.mousedownTarget === dialog) {
            // TODO: The 'emit' function requires a mandatory void argument
            this.requestClose.emit();
        }
    }

    ngAfterViewInit(): void {
        this.syncOpenState();
        this.initDrag();
    }

    ngOnChanges(changes: SimpleChanges): void {
        if ('open' in changes) {
            this.syncOpenState();
        }
    }

    ngOnDestroy(): void {
        this.teardownDrag();
    }

    private syncOpenState() {
        const dialog = this.dialogEl()?.nativeElement;
        if (!dialog) return;
        try {
            const open = this.open();
            if (open && !dialog.open) {
                this.resetDragPosition();
                dialog.showModal();
                this.markDragHandle();
            } else if (!open && dialog.open) {
                dialog.close();
            }
        } catch {
            // In case showModal throws due to DOM not ready yet, try on next frame
            requestAnimationFrame(() => {
                try {
                    const open = this.open();
                    if (open && !dialog.open) { this.resetDragPosition(); dialog.showModal(); this.markDragHandle(); }
                    else if (!open && dialog.open) dialog.close();
                } catch { /* no-op */ }
            });
        }
    }

    // ── Dragging ──────────────────────────────────────────────────────────
    // The native <dialog> lives in the top layer; moving it is a plain
    // `transform: translate()` on the dialog element (the same technique CDK
    // uses elsewhere in the app). We roll our own tiny pointer drag so every
    // consumer gets it for free — no per-modal cdkDrag wiring.

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
    private dialogWidth = 0;
    private dialogHeight = 0;
    /** Lives for the component's lifetime (the pointerdown listener). */
    private readonly disposers: Array<() => void> = [];
    /** Lives only for a single drag gesture (move/up/cancel); cleared on release. */
    private dragSessionDisposers: Array<() => void> = [];

    private initDrag(): void {
        const dialog = this.dialogEl()?.nativeElement;
        if (!dialog) return;
        this.disposers.push(
            this.renderer.listen(dialog, 'pointerdown', (e: PointerEvent) => this.onDragPointerDown(e)),
        );
        this.markDragHandle();
    }

    private teardownDrag(): void {
        this.endDragSession();
        for (const dispose of this.disposers) dispose();
        this.disposers.length = 0;
    }

    private endDragSession(): void {
        for (const dispose of this.dragSessionDisposers) dispose();
        this.dragSessionDisposers = [];
    }

    /** Resolve the header handle within the projected content. */
    private resolveHandle(dialog: HTMLDialogElement): HTMLElement | null {
        const content = dialog.querySelector<HTMLElement>('.modal-content') ?? dialog;
        return (
            content.querySelector<HTMLElement>('[data-tsic-drag-handle]') ??
            content.querySelector<HTMLElement>('.modal-header') ??
            (content.firstElementChild as HTMLElement | null)
        );
    }

    /** Tag the resolved handle and give it the grab cursor. Idempotent. */
    private markDragHandle(): void {
        if (!this.draggable()) return;
        const dialog = this.dialogEl()?.nativeElement;
        if (!dialog) return;
        // Content may not be projected on the exact frame we open; defer one frame.
        requestAnimationFrame(() => {
            const handle = this.resolveHandle(dialog);
            if (handle) {
                this.renderer.setAttribute(handle, 'data-tsic-drag-handle', 'true');
                this.renderer.setStyle(handle, 'cursor', 'grab');
            }
        });
    }

    private onDragPointerDown(e: PointerEvent): void {
        if (!this.draggable() || e.button !== 0) return;
        const dialog = this.dialogEl().nativeElement;
        const handle = this.resolveHandle(dialog);
        if (!handle) return;

        const target = e.target as HTMLElement | null;
        if (!target || !handle.contains(target)) return;
        // Never hijack interactive controls that live in the header.
        if (target.closest('button, a, input, select, textarea, label, [contenteditable="true"]')) return;

        const rect = dialog.getBoundingClientRect();
        this.naturalLeft = rect.left - this.offsetX;
        this.naturalTop = rect.top - this.offsetY;
        this.dialogWidth = rect.width;
        this.dialogHeight = rect.height;

        this.dragging = true;
        this.pointerId = e.pointerId;
        this.startClientX = e.clientX;
        this.startClientY = e.clientY;
        this.baseX = this.offsetX;
        this.baseY = this.offsetY;

        try { handle.setPointerCapture(e.pointerId); } catch { /* no-op */ }
        this.renderer.addClass(dialog, 'is-dragging');
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
        const dialog = this.dialogEl().nativeElement;

        let nx = this.baseX + (e.clientX - this.startClientX);
        let ny = this.baseY + (e.clientY - this.startClientY);

        // Clamp so the dialog stays within the viewport. If the dialog is larger
        // than the viewport on an axis, the min/max invert — Math.min/max on the
        // bounds keeps the range valid either way.
        const minX = -this.naturalLeft;
        const maxX = window.innerWidth - this.naturalLeft - this.dialogWidth;
        const minY = -this.naturalTop;
        const maxY = window.innerHeight - this.naturalTop - this.dialogHeight;
        nx = Math.min(Math.max(nx, Math.min(minX, maxX)), Math.max(minX, maxX));
        ny = Math.min(Math.max(ny, Math.min(minY, maxY)), Math.max(minY, maxY));

        this.offsetX = nx;
        this.offsetY = ny;
        this.renderer.setStyle(dialog, 'transform', `translate(${nx}px, ${ny}px)`);
    }

    private onDragPointerUp(e: PointerEvent): void {
        if (e.pointerId !== this.pointerId) return;
        this.dragging = false;
        this.pointerId = null;
        const dialog = this.dialogEl().nativeElement;
        this.renderer.removeClass(dialog, 'is-dragging');
        const handle = e.currentTarget as HTMLElement | null;
        if (handle) this.renderer.setStyle(handle, 'cursor', 'grab');
        this.endDragSession();
    }

    /** Recenter the modal each time it reopens. */
    private resetDragPosition(): void {
        this.offsetX = 0;
        this.offsetY = 0;
        const dialog = this.dialogEl()?.nativeElement;
        if (dialog) this.renderer.removeStyle(dialog, 'transform');
    }
}
