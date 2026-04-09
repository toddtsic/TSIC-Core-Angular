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
        <tsic-dialog [open]="true" size="md" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Bulk Generate Discount Codes</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <fieldset class="config-fieldset">
                        <legend>Code Pattern</legend>
                        <div class="row g-2">
                            <div class="col-md-3">
                                <label for="prefix" class="field-label">Prefix</label>
                                <input id="prefix" type="text" class="field-input"
                                    placeholder="e.g., SUMMER"
                                    [value]="prefix()"
                                    (input)="prefix.set(($any($event.target).value))"
                                    maxlength="20" />
                            </div>
                            <div class="col-md-3">
                                <label for="suffix" class="field-label">Suffix</label>
                                <input id="suffix" type="text" class="field-input"
                                    placeholder="e.g., 2026"
                                    [value]="suffix()"
                                    (input)="suffix.set(($any($event.target).value))"
                                    maxlength="20" />
                            </div>
                            <div class="col-md-3">
                                <label for="startNumber" class="field-label">Start #</label>
                                <input id="startNumber" type="number" class="field-input"
                                    [value]="startNumber()"
                                    (input)="startNumber.set(+($any($event.target).value))"
                                    min="1" max="9999" />
                            </div>
                            <div class="col-md-3">
                                <label for="count" class="field-label">Count <span class="text-body-secondary">(max 500)</span></label>
                                <input id="count" type="number" class="field-input"
                                    [value]="count()"
                                    (input)="count.set(+($any($event.target).value))"
                                    min="1" max="500"
                                    [class.is-invalid]="count() > 500" />
                                @if (count() > 500) {
                                    <div class="field-error">Maximum 500 codes per bulk generation.</div>
                                }
                            </div>
                            <div class="col-12">
                                <p class="field-help mb-0">
                                    Preview: <code>{{ previewFirst() }}</code>
                                    @if (count() > 1) {
                                        <span class="mx-1">...</span>
                                        <code>{{ previewLast() }}</code>
                                    }
                                    &mdash; {{ count() }} code(s)
                                </p>
                            </div>
                        </div>
                    </fieldset>

                    <fieldset class="config-fieldset mt-3">
                        <legend>Discount Settings</legend>
                        <div class="row g-2">
                            <div class="col-md-6">
                                <label class="field-label">Discount Type</label>
                                <div class="btn-group d-flex" role="group">
                                    <input type="radio" class="btn-check" id="bulkTypeDollar" value="DollarAmount"
                                           [checked]="discountType() === 'DollarAmount'"
                                           (change)="discountType.set('DollarAmount')" />
                                    <label class="btn btn-outline-success btn-sm" for="bulkTypeDollar">
                                        <i class="bi bi-currency-dollar me-1"></i>Dollar
                                    </label>
                                    <input type="radio" class="btn-check" id="bulkTypePercent" value="Percentage"
                                           [checked]="discountType() === 'Percentage'"
                                           (change)="discountType.set('Percentage')" />
                                    <label class="btn btn-outline-info btn-sm" for="bulkTypePercent">
                                        <i class="bi bi-percent me-1"></i>Percent
                                    </label>
                                </div>
                            </div>
                            <div class="col-md-6">
                                <label for="bulkAmount" class="field-label">
                                    Amount
                                    @if (discountType() === 'Percentage') {
                                        <span class="text-body-secondary">(0-100%)</span>
                                    }
                                </label>
                                <div class="input-group input-group-sm">
                                    @if (discountType() === 'DollarAmount') {
                                        <span class="input-group-text">$</span>
                                    }
                                    <input id="bulkAmount" type="number" class="field-input"
                                        [value]="amount()"
                                        (input)="amount.set(+($any($event.target).value))"
                                        step="0.01" min="0.01"
                                        [max]="discountType() === 'Percentage' ? 100 : 999999" />
                                    @if (discountType() === 'Percentage') {
                                        <span class="input-group-text">%</span>
                                    }
                                </div>
                            </div>
                            <div class="col-md-6">
                                <label for="bulkStartDate" class="field-label">Start Date</label>
                                <input id="bulkStartDate" type="date" class="field-input"
                                    [value]="startDate()"
                                    (input)="startDate.set(($any($event.target).value))" />
                            </div>
                            <div class="col-md-6">
                                <label for="bulkEndDate" class="field-label">End Date</label>
                                <input id="bulkEndDate" type="date" class="field-input"
                                    [value]="endDate()"
                                    (input)="endDate.set(($any($event.target).value))"
                                    [class.is-invalid]="endDate() <= startDate()" />
                                @if (endDate() <= startDate()) {
                                    <div class="field-error">End date must be after start date.</div>
                                }
                            </div>
                        </div>
                    </fieldset>

                    @if (count() > 50) {
                        <div class="alert alert-warning mt-3 mb-0 py-2">
                            <i class="bi bi-exclamation-triangle-fill me-2"></i>
                            You are about to create {{ count() }} discount codes. This cannot be undone.
                        </div>
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
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
