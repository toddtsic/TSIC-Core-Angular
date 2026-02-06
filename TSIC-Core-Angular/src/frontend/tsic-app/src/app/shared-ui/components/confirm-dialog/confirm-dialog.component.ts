import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TsicDialogComponent } from '../tsic-dialog/tsic-dialog.component';

@Component({
    selector: 'confirm-dialog',
    standalone: true,
    imports: [TsicDialogComponent],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="cancelled.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ title }}</h5>
                    <button type="button" class="btn-close" (click)="cancelled.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <p class="mb-0">{{ message }}</p>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="cancelled.emit()">
                        {{ cancelLabel }}
                    </button>
                    <button type="button"
                        class="btn btn-sm"
                        [class.btn-danger]="confirmVariant === 'danger'"
                        [class.btn-warning]="confirmVariant === 'warning'"
                        [class.btn-primary]="confirmVariant === 'primary'"
                        (click)="confirmed.emit()">
                        {{ confirmLabel }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `
})
export class ConfirmDialogComponent {
    @Input() title = 'Confirm';
    @Input() message = 'Are you sure?';
    @Input() confirmLabel = 'Confirm';
    @Input() cancelLabel = 'Cancel';
    @Input() confirmVariant: 'danger' | 'warning' | 'primary' = 'primary';

    @Output() confirmed = new EventEmitter<void>();
    @Output() cancelled = new EventEmitter<void>();
}
