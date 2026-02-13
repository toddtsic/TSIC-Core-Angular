import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ElementRef, ViewChild, AfterViewInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FocusTrapDirective } from '../../directives/focus-trap.directive';

/**
 * Reusable wrapper for native <dialog> with an integrated focus trap and ESC-to-close.
 * Usage:
 *  <tsic-dialog [open]="true" size="lg" (requestClose)="onClose()">
 *    <div class="modal-content"> ... </div>
 *  </tsic-dialog>
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
      (click)="onBackdropClick($event)"
      [tsicFocusTrap]="true"
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
                    box-shadow: 0 0.75rem 1.5rem rgba(0,0,0,.25);
                    background: transparent; /* content provides background */
                }
                .tsic-dialog.tsic-dialog-sm { width: min(480px, 96vw); }
                .tsic-dialog.tsic-dialog-lg { width: min(960px, 96vw); }

                        /* Content chrome (Bootstrap-like) - use deep selector so projected content is styled */
                        :host ::ng-deep .modal-content {
                    border: 1px solid rgba(0,0,0,.1);
                    border-radius: 0.5rem;
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
                    padding: 1rem; 
                    /* Fallback background so dialogs without .modal-content aren't translucent */
                    background: var(--bs-body-bg, #fff);
                }
                        :host ::ng-deep .modal-header { 
                    display: flex; align-items: center; justify-content: space-between;
                    border-bottom: 1px solid rgba(0,0,0,.1);
                }
                        :host ::ng-deep .modal-footer { 
                    display: flex; justify-content: flex-end; gap: .5rem;
                    border-top: 1px solid rgba(0,0,0,.1);
                }
                        :host ::ng-deep .modal-title { margin: 0; font-size: 1.1rem; font-weight: 600; }
                        :host ::ng-deep .btn-close { box-shadow: none !important; outline: none !important; }

                /* Backdrop */
                .tsic-dialog::backdrop {
                    background: rgba(0,0,0,.5);
                    animation: tsicDialogFadeIn .12s ease-out;
                }
                @keyframes tsicDialogFadeIn { from { opacity: 0; } to { opacity: 1; } }
                @keyframes tsicDialogContentIn { from { opacity: 0; transform: scale(.98); } to { opacity: 1; transform: scale(1); } }
                `
    ],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicDialogComponent implements AfterViewInit, OnChanges {
    /** Controls the native <dialog> open state. Often left as true when wrapped in an @if block. */
    @Input() open = true;
    /** Size variant to add modifier class (e.g., tsic-dialog-lg). */
    @Input() size: 'sm' | 'md' | 'lg' | '' = '';
    /** Whether pressing ESC should emit requestClose (default true). */
    @Input() closeOnEsc = true;
    /** Close the dialog when clicking on the backdrop area (outside content). Default true. */
    @Input() closeOnBackdrop = true;

    @Output() requestClose = new EventEmitter<void>();

    @ViewChild('dlg', { static: true }) dialogEl!: ElementRef<HTMLDialogElement>;

    get sizeClass() {
        return {
            'tsic-dialog-sm': this.size === 'sm',
            'tsic-dialog-lg': this.size === 'lg',
        };
    }

    onEsc() {
        if (this.closeOnEsc) {
            this.requestClose.emit();
        }
    }

    onBackdropClick(event: MouseEvent) {
        if (!this.closeOnBackdrop) return;
        const dialog = this.dialogEl?.nativeElement;
        if (!dialog) return;
        // Native <dialog> forwards backdrop clicks to the dialog element itself
        if (event.target === dialog) {
            this.requestClose.emit();
        }
    }

    ngAfterViewInit(): void {
        this.syncOpenState();
    }

    ngOnChanges(changes: SimpleChanges): void {
        if ('open' in changes) {
            this.syncOpenState();
        }
    }

    private syncOpenState() {
        const dialog = this.dialogEl?.nativeElement;
        if (!dialog) return;
        try {
            if (this.open && !dialog.open) {
                dialog.showModal();
            } else if (!this.open && dialog.open) {
                dialog.close();
            }
        } catch {
            // In case showModal throws due to DOM not ready yet, try on next frame
            requestAnimationFrame(() => {
                try {
                    if (this.open && !dialog.open) dialog.showModal();
                    else if (!this.open && dialog.open) dialog.close();
                } catch { /* no-op */ }
            });
        }
    }
}
