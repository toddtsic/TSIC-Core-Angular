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
        <tsic-dialog [open]="true" size="md" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Age Range' : 'Edit Age Range' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <!-- Range Name -->
                    <div class="mb-3">
                        <label for="rangeName" class="form-label fw-semibold">Range Name</label>
                        <input
                            id="rangeName"
                            type="text"
                            class="form-control"
                            placeholder="e.g. U12, U14, Open"
                            [value]="rangeName()"
                            (input)="rangeName.set($any($event.target).value)"
                            maxlength="100" />
                    </div>

                    <!-- Date Range -->
                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label for="rangeLeft" class="form-label fw-semibold">Start Date (DOB From)</label>
                            <input
                                id="rangeLeft"
                                type="date"
                                class="form-control"
                                [value]="rangeLeft()"
                                (input)="rangeLeft.set($any($event.target).value)" />
                        </div>
                        <div class="col-md-6 mb-3">
                            <label for="rangeRight" class="form-label fw-semibold">End Date (DOB To)</label>
                            <input
                                id="rangeRight"
                                type="date"
                                class="form-control"
                                [value]="rangeRight()"
                                (input)="rangeRight.set($any($event.target).value)"
                                [class.is-invalid]="rangeRight().length > 0 && rangeLeft().length > 0 && rangeRight() < rangeLeft()" />
                            @if (rangeRight().length > 0 && rangeLeft().length > 0 && rangeRight() < rangeLeft()) {
                                <div class="invalid-feedback">End date must be on or after start date.</div>
                            }
                        </div>
                    </div>

                    <!-- Help text -->
                    <div class="alert alert-info d-flex align-items-start py-2 mb-0" role="note">
                        <i class="bi bi-info-circle me-2 mt-1"></i>
                        <small>
                            Age ranges define DOB windows. Players whose date of birth falls
                            within a range will be restricted to teams assigned to that range.
                        </small>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
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
