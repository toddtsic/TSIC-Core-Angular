import { Component, EventEmitter, Input, Output } from '@angular/core';
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
      class="tsic-dialog"
      [ngClass]="sizeClass"
      [open]="open"
      (keydown.escape)="onEsc()"
      [tsicFocusTrap]="true"
    >
      <ng-content></ng-content>
    </dialog>
  `,
})
export class TsicDialogComponent {
    /** Controls the native <dialog> open state. Often left as true when wrapped in an @if block. */
    @Input() open = true;
    /** Size variant to add modifier class (e.g., tsic-dialog-lg). */
    @Input() size: 'sm' | 'md' | 'lg' | '' = '';
    /** Whether pressing ESC should emit requestClose (default true). */
    @Input() closeOnEsc = true;

    @Output() requestClose = new EventEmitter<void>();

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
}
