import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { DiscountCodeService } from '../services/discount-code.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import type { BulkAddDiscountCodeRequest } from '@core/api';

@Component({
    selector: 'bulk-code-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="lg" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Bulk Generate Discount Codes</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <!-- Pattern Section -->
                    <div class="card mb-3">
                        <div class="card-body">
                            <h6 class="card-title fw-semibold mb-3">Code Pattern</h6>
                            <div class="row">
                                <div class="col-md-4 mb-3">
                                    <label for="prefix" class="form-label">Prefix</label>
                                    <input
                                        id="prefix"
                                        type="text"
                                        class="form-control"
                                        placeholder="e.g., SUMMER"
                                        [value]="prefix()"
                                        (input)="prefix.set(($any($event.target).value))"
                                        maxlength="20" />
                                </div>
                                <div class="col-md-4 mb-3">
                                    <label for="suffix" class="form-label">Suffix</label>
                                    <input
                                        id="suffix"
                                        type="text"
                                        class="form-control"
                                        placeholder="e.g., 2026"
                                        [value]="suffix()"
                                        (input)="suffix.set(($any($event.target).value))"
                                        maxlength="20" />
                                </div>
                            </div>
                            <div class="row">
                                <div class="col-md-4 mb-3">
                                    <label for="startNumber" class="form-label">Start Number</label>
                                    <input
                                        id="startNumber"
                                        type="number"
                                        class="form-control"
                                        [value]="startNumber()"
                                        (input)="startNumber.set(+($any($event.target).value))"
                                        min="1"
                                        max="9999" />
                                </div>
                                <div class="col-md-4 mb-3">
                                    <label for="count" class="form-label">
                                        Count 
                                        <small class="text-body-secondary">(max 500)</small>
                                    </label>
                                    <input
                                        id="count"
                                        type="number"
                                        class="form-control"
                                        [value]="count()"
                                        (input)="count.set(+($any($event.target).value))"
                                        min="1"
                                        max="500"
                                        [class.is-invalid]="count() > 500" />
                                    @if (count() > 500) {
                                        <div class="invalid-feedback">Maximum 500 codes per bulk generation.</div>
                                    }
                                </div>
                            </div>

                            <!-- Preview -->
                            <div class="alert alert-info mb-0">
                                <strong>Preview:</strong>
                                <div class="mt-2">
                                    <code>{{ previewFirst() }}</code>
                                    @if (count() > 1) {
                                        <span class="mx-2">...</span>
                                        <code>{{ previewLast() }}</code>
                                    }
                                </div>
                                <small class="text-body-secondary d-block mt-2">
                                    {{ count() }} code(s) will be created
                                </small>
                            </div>
                        </div>
                    </div>

                    <!-- Discount Settings -->
                    <div class="card mb-3">
                        <div class="card-body">
                            <h6 class="card-title fw-semibold mb-3">Discount Settings (Applied to All Codes)</h6>
                            
                            <!-- Discount Type -->
                            <div class="mb-3">
                                <label class="form-label fw-semibold">Discount Type</label>
                                <div class="btn-group d-flex" role="group">
                                    <input type="radio" class="btn-check" id="bulkTypeDollar" value="DollarAmount" 
                                           [checked]="discountType() === 'DollarAmount'"
                                           (change)="discountType.set('DollarAmount')" />
                                    <label class="btn btn-outline-success" for="bulkTypeDollar">
                                        <i class="bi bi-currency-dollar me-1"></i>Dollar Amount
                                    </label>

                                    <input type="radio" class="btn-check" id="bulkTypePercent" value="Percentage"
                                           [checked]="discountType() === 'Percentage'"
                                           (change)="discountType.set('Percentage')" />
                                    <label class="btn btn-outline-info" for="bulkTypePercent">
                                        <i class="bi bi-percent me-1"></i>Percentage
                                    </label>
                                </div>
                            </div>

                            <!-- Amount -->
                            <div class="mb-3">
                                <label for="bulkAmount" class="form-label fw-semibold">
                                    Amount 
                                    @if (discountType() === 'Percentage') {
                                        <small class="text-body-secondary">(0-100%)</small>
                                    }
                                </label>
                                <div class="input-group">
                                    @if (discountType() === 'DollarAmount') {
                                        <span class="input-group-text">$</span>
                                    }
                                    <input
                                        id="bulkAmount"
                                        type="number"
                                        class="form-control"
                                        [value]="amount()"
                                        (input)="amount.set(+($any($event.target).value))"
                                        step="0.01"
                                        min="0.01"
                                        [max]="discountType() === 'Percentage' ? 100 : 999999" />
                                    @if (discountType() === 'Percentage') {
                                        <span class="input-group-text">%</span>
                                    }
                                </div>
                            </div>

                            <!-- Date Range -->
                            <div class="row">
                                <div class="col-md-6 mb-3">
                                    <label for="bulkStartDate" class="form-label fw-semibold">Start Date</label>
                                    <input
                                        id="bulkStartDate"
                                        type="date"
                                        class="form-control"
                                        [value]="startDate()"
                                        (input)="startDate.set(($any($event.target).value))" />
                                </div>
                                <div class="col-md-6 mb-3">
                                    <label for="bulkEndDate" class="form-label fw-semibold">End Date</label>
                                    <input
                                        id="bulkEndDate"
                                        type="date"
                                        class="form-control"
                                        [value]="endDate()"
                                        (input)="endDate.set(($any($event.target).value))"
                                        [class.is-invalid]="endDate() <= startDate()" />
                                    @if (endDate() <= startDate()) {
                                        <div class="invalid-feedback">End date must be after start date.</div>
                                    }
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Warning -->
                    @if (count() > 50) {
                        <div class="alert alert-warning">
                            <i class="bi bi-exclamation-triangle-fill me-2"></i>
                            <strong>Warning:</strong> You are about to create {{ count() }} discount codes. This operation cannot be undone.
                        </div>
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
                        @if (isSaving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        Generate {{ count() }} Code(s)
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulkCodeModalComponent implements OnInit {
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<void>();

    private readonly discountCodeService = inject(DiscountCodeService);
    private readonly toastService = inject(ToastService);

    // Pattern fields
    prefix = signal('');
    suffix = signal('');
    startNumber = signal(1);
    count = signal(10);

    // Discount settings
    discountType = signal<'Percentage' | 'DollarAmount'>('DollarAmount');
    amount = signal(0);
    startDate = signal('');
    endDate = signal('');

    isSaving = signal(false);

    // Computed preview
    previewFirst = computed(() => {
        const num = this.startNumber().toString().padStart(3, '0');
        return `${this.prefix()}${num}${this.suffix()}`.trim();
    });

    previewLast = computed(() => {
        const num = (this.startNumber() + this.count() - 1).toString().padStart(3, '0');
        return `${this.prefix()}${num}${this.suffix()}`.trim();
    });

    ngOnInit(): void {
        // Default to tomorrow and 30 days out
        const today = new Date();
        const tomorrow = new Date(today);
        tomorrow.setDate(tomorrow.getDate() + 1);
        const monthOut = new Date(today);
        monthOut.setDate(monthOut.getDate() + 30);
        
        this.startDate.set(this.formatDateForInput(tomorrow.toISOString()));
        this.endDate.set(this.formatDateForInput(monthOut.toISOString()));
    }

    isValid(): boolean {
        return this.count() > 0 &&
               this.count() <= 500 &&
               this.amount() > 0 &&
               this.startDate().length > 0 &&
               this.endDate().length > 0 &&
               this.endDate() > this.startDate();
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;

        this.isSaving.set(true);

        const request: BulkAddDiscountCodeRequest = {
            prefix: this.prefix(),
            suffix: this.suffix(),
            startNumber: this.startNumber(),
            count: this.count(),
            discountType: this.discountType(),
            amount: this.amount(),
            startDate: new Date(this.startDate()).toISOString(),
            endDate: new Date(this.endDate()).toISOString()
        };

        this.discountCodeService.bulkAddDiscountCodes(request).subscribe({
            next: (codes) => {
                this.toastService.show(`${codes.length} discount code(s) generated`, 'success');
                this.saved.emit();
            },
            error: (error) => {
                this.toastService.show(error.error?.message || 'Bulk generation failed', 'danger');
                this.isSaving.set(false);
            }
        });
    }

    private formatDateForInput(isoDate: string): string {
        const date = new Date(isoDate);
        return date.toISOString().split('T')[0];
    }
}
