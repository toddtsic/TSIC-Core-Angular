import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { AgeRangeAdminService } from '../services/age-range-admin.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import type { AgeRangeDto, CreateAgeRangeRequest, UpdateAgeRangeRequest } from '@core/api';

export type ModalMode = 'add' | 'edit';

@Component({
    selector: 'age-range-form-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Age Range' : 'Edit Age Range' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="row g-2">
                        <div class="col-12">
                            <label for="rangeName" class="field-label">Range Name</label>
                            <input id="rangeName" type="text" class="field-input"
                                placeholder="e.g. U12, U14, Open"
                                [value]="rangeName()"
                                (input)="rangeName.set($any($event.target).value)"
                                maxlength="100" />
                        </div>
                        <div class="col-md-6">
                            <label for="rangeLeft" class="field-label">DOB From</label>
                            <input id="rangeLeft" type="date" class="field-input"
                                [value]="rangeLeft()"
                                (input)="rangeLeft.set($any($event.target).value)" />
                        </div>
                        <div class="col-md-6">
                            <label for="rangeRight" class="field-label">DOB To</label>
                            <input id="rangeRight" type="date" class="field-input"
                                [value]="rangeRight()"
                                (input)="rangeRight.set($any($event.target).value)"
                                [class.is-invalid]="rangeRight().length > 0 && rangeLeft().length > 0 && rangeRight() < rangeLeft()" />
                            @if (rangeRight().length > 0 && rangeLeft().length > 0 && rangeRight() < rangeLeft()) {
                                <div class="field-error">End date must be on or after start date.</div>
                            }
                        </div>
                    </div>
                    <p class="field-help mt-2 mb-0">
                        <i class="bi bi-info-circle me-1"></i>
                        Players whose DOB falls within this range will be restricted to teams assigned to it.
                    </p>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
                        @if (isSaving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        {{ mode === 'add' ? 'Add Range' : 'Save Changes' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AgeRangeFormModalComponent implements OnInit {
    @Input() mode: ModalMode = 'add';
    @Input() ageRange: AgeRangeDto | null = null;
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<void>();

    private readonly ageRangeService = inject(AgeRangeAdminService);
    private readonly toastService = inject(ToastService);

    // Form fields
    rangeName = signal('');
    rangeLeft = signal('');
    rangeRight = signal('');

    // Saving state
    isSaving = signal(false);

    ngOnInit(): void {
        if (this.mode === 'edit' && this.ageRange) {
            this.rangeName.set(this.ageRange.rangeName ?? '');
            this.rangeLeft.set(this.formatDateForInput(this.ageRange.rangeLeft));
            this.rangeRight.set(this.formatDateForInput(this.ageRange.rangeRight));
        }
    }

    isValid(): boolean {
        const hasName = this.rangeName().trim().length > 0;
        const hasLeft = this.rangeLeft().length > 0;
        const hasRight = this.rangeRight().length > 0;
        const datesValid = !(hasLeft && hasRight && this.rangeRight() < this.rangeLeft());
        return hasName && hasLeft && hasRight && datesValid;
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;

        this.isSaving.set(true);

        if (this.mode === 'add') {
            const request: CreateAgeRangeRequest = {
                rangeName: this.rangeName().trim(),
                rangeLeft: new Date(this.rangeLeft()).toISOString(),
                rangeRight: new Date(this.rangeRight()).toISOString()
            };

            this.ageRangeService.createAgeRange(request).subscribe({
                next: () => {
                    this.toastService.show(`Age range "${this.rangeName()}" created`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to create age range', 'danger');
                    this.isSaving.set(false);
                }
            });
        } else if (this.ageRange) {
            const request: UpdateAgeRangeRequest = {
                rangeName: this.rangeName().trim(),
                rangeLeft: new Date(this.rangeLeft()).toISOString(),
                rangeRight: new Date(this.rangeRight()).toISOString()
            };

            this.ageRangeService.updateAgeRange(this.ageRange.ageRangeId, request).subscribe({
                next: () => {
                    this.toastService.show(`Age range "${this.rangeName()}" updated`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to update age range', 'danger');
                    this.isSaving.set(false);
                }
            });
        }
    }

    private formatDateForInput(isoDate: string | null | undefined): string {
        if (!isoDate) return '';
        const date = new Date(isoDate);
        return date.toISOString().split('T')[0];
    }
}
