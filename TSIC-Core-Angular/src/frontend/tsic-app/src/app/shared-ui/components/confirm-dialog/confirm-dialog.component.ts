import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '../tsic-dialog/tsic-dialog.component';

@Component({
    selector: 'confirm-dialog',
    standalone: true,
    imports: [TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="cancelled.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ title }}</h5>
                    <button type="button" class="btn-close" (click)="cancelled.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div [innerHTML]="message"></div>
                    @if (requireConfirmText) {
                        <div class="confirm-text-guard">
                            <label for="confirmTextInput" class="form-label text-muted">
                                Type <strong>CONFIRM</strong> to proceed
                            </label>
                            <input id="confirmTextInput"
                                   type="text"
                                   class="form-control"
                                   placeholder="CONFIRM"
                                   autocomplete="off"
                                   [ngModel]="confirmInput()"
                                   (ngModelChange)="confirmInput.set($event)">
                        </div>
                    }
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
                        [disabled]="requireConfirmText && confirmInput().toUpperCase() !== 'CONFIRM'"
                        (click)="confirmed.emit()">
                        {{ confirmLabel }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        .confirm-text-guard {
            margin-top: var(--space-4);

            .form-label {
                font-size: var(--font-size-sm);
                margin-bottom: var(--space-2);
            }

            .form-control {
                max-width: 240px;
                font-weight: var(--font-weight-semibold);
                letter-spacing: 0.1em;
                text-transform: uppercase;
            }
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConfirmDialogComponent {
    @Input() title = 'Confirm';
    @Input() message = 'Are you sure?';
    @Input() confirmLabel = 'Confirm';
    @Input() cancelLabel = 'Cancel';
    @Input() confirmVariant: 'danger' | 'warning' | 'primary' = 'primary';
    @Input() requireConfirmText = false;

    @Output() confirmed = new EventEmitter<void>();
    @Output() cancelled = new EventEmitter<void>();

    confirmInput = signal('');
}
