import { Component, EventEmitter, Input, Output, inject, signal, OnInit, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { DiscountCodeService } from '../services/discount-code.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import type { DiscountCodeDto, AddDiscountCodeRequest, UpdateDiscountCodeRequest } from '@core/api';

export type ModalMode = 'add' | 'edit';

@Component({
    selector: 'code-form-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="md" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Discount Code' : 'Edit Discount Code' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <!-- Code Name -->
                    <div class="mb-3">
                        <label for="codeName" class="form-label fw-semibold">Code Name</label>
                        <input
                            id="codeName"
                            type="text"
                            class="form-control"
                            placeholder="e.g., SUMMER2026"
                            [value]="codeName()"
                            (input)="onCodeNameInput($event)"
                            [disabled]="mode === 'edit'"
                            [class.is-invalid]="codeExists()"
                            maxlength="50" />
                        @if (codeExists()) {
                            <div class="invalid-feedback">This code already exists.</div>
                        }
                    </div>

                    <!-- Discount Type -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Discount Type</label>
                        <div class="btn-group d-flex" role="group">
                            <input type="radio" class="btn-check" id="typeDollar" value="DollarAmount" 
                                   [checked]="discountType() === 'DollarAmount'"
                                   (change)="discountType.set('DollarAmount')" />
                            <label class="btn btn-outline-success" for="typeDollar">
                                <i class="bi bi-currency-dollar me-1"></i>Dollar Amount
                            </label>

                            <input type="radio" class="btn-check" id="typePercent" value="Percentage"
                                   [checked]="discountType() === 'Percentage'"
                                   (change)="discountType.set('Percentage')" />
                            <label class="btn btn-outline-info" for="typePercent">
                                <i class="bi bi-percent me-1"></i>Percentage
                            </label>
                        </div>
                    </div>

                    <!-- Amount -->
                    <div class="mb-3">
                        <label for="amount" class="form-label fw-semibold">
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
                                id="amount"
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
                            <label for="startDate" class="form-label fw-semibold">Start Date</label>
                            <input
                                id="startDate"
                                type="date"
                                class="form-control"
                                [value]="startDate()"
                                (input)="startDate.set(($any($event.target).value))" />
                        </div>
                        <div class="col-md-6 mb-3">
                            <label for="endDate" class="form-label fw-semibold">End Date</label>
                            <input
                                id="endDate"
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

                    @if (mode === 'edit') {
                        <!-- Active Toggle (edit only) -->
                        <div class="mb-3">
                            <label for="activeToggle" class="form-label fw-semibold">Active</label>
                            <div class="form-check form-switch">
                                <input id="activeToggle" type="checkbox" class="form-check-input" role="switch"
                                    [checked]="isActive()"
                                    (change)="isActive.set($any($event.target).checked)" />
                                <label class="form-check-label" for="activeToggle">
                                    {{ isActive() ? 'Active' : 'Inactive' }}
                                </label>
                            </div>
                        </div>

                        <!-- Usage Count (read-only) -->
                        @if (code) {
                            <div class="mb-3">
                                <label class="form-label fw-semibold">Usage Count</label>
                                <p class="form-control-plaintext">
                                    {{ code.usageCount }} 
                                    @if (code.usageCount > 0) {
                                        <span class="text-body-secondary">time(s)</span>
                                    }
                                </p>
                            </div>
                        }
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
                        @if (isSaving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        {{ mode === 'add' ? 'Add Code' : 'Save Changes' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        .typeahead-dropdown {
            max-height: 200px;
            overflow-y: auto;
            position: absolute;
            z-index: 1000;
        }
    `]
})
export class CodeFormModalComponent implements OnInit {
    @Input() mode: ModalMode = 'add';
    @Input() code: DiscountCodeDto | null = null;
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<void>();

    private readonly discountCodeService = inject(DiscountCodeService);
    private readonly toastService = inject(ToastService);

    // Form fields
    codeName = signal('');
    discountType = signal<'Percentage' | 'DollarAmount'>('DollarAmount');
    amount = signal(0);
    startDate = signal('');
    endDate = signal('');
    isActive = signal(true);

    // Validation
    codeExists = signal(false);
    isSaving = signal(false);

    constructor() {
        // Check for duplicate code names on input (add mode only)
        effect(() => {
            const name = this.codeName();
            if (this.mode === 'add' && name.length >= 3) {
                this.discountCodeService.checkCodeExists(name).subscribe({
                    next: (result) => this.codeExists.set(result.exists),
                    error: () => this.codeExists.set(false)
                });
            }
        });
    }

    ngOnInit(): void {
        if (this.mode === 'edit' && this.code) {
            this.codeName.set(this.code.codeName);
            this.discountType.set(this.code.discountType as 'Percentage' | 'DollarAmount');
            this.amount.set(this.code.amount);
            this.startDate.set(this.formatDateForInput(this.code.startDate));
            this.endDate.set(this.formatDateForInput(this.code.endDate));
            this.isActive.set(this.code.isActive);
        } else {
            // Default to tomorrow and 30 days out
            const today = new Date();
            const tomorrow = new Date(today);
            tomorrow.setDate(tomorrow.getDate() + 1);
            const monthOut = new Date(today);
            monthOut.setDate(monthOut.getDate() + 30);
            
            this.startDate.set(this.formatDateForInput(tomorrow.toISOString()));
            this.endDate.set(this.formatDateForInput(monthOut.toISOString()));
        }
    }

    onCodeNameInput(event: Event): void {
        const value = (event.target as HTMLInputElement).value;
        this.codeName.set(value);
    }

    isValid(): boolean {
        return this.codeName().length > 0 &&
               this.amount() > 0 &&
               this.startDate().length > 0 &&
               this.endDate().length > 0 &&
               this.endDate() > this.startDate() &&
               !this.codeExists();
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;

        this.isSaving.set(true);

        if (this.mode === 'add') {
            const request: AddDiscountCodeRequest = {
                codeName: this.codeName(),
                discountType: this.discountType(),
                amount: this.amount(),
                startDate: new Date(this.startDate()).toISOString(),
                endDate: new Date(this.endDate()).toISOString()
            };

            this.discountCodeService.addDiscountCode(request).subscribe({
                next: () => {
                    this.toastService.show(`Discount code "${this.codeName()}" created`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to create discount code', 'danger');
                    this.isSaving.set(false);
                }
            });
        } else if (this.code) {
            const request: UpdateDiscountCodeRequest = {
                discountType: this.discountType(),
                amount: this.amount(),
                startDate: new Date(this.startDate()).toISOString(),
                endDate: new Date(this.endDate()).toISOString(),
                isActive: this.isActive()
            };

            this.discountCodeService.updateDiscountCode(this.code.ai, request).subscribe({
                next: () => {
                    this.toastService.show(`Discount code "${this.codeName()}" updated`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to update discount code', 'danger');
                    this.isSaving.set(false);
                }
            });
        }
    }

    private formatDateForInput(isoDate: string): string {
        const date = new Date(isoDate);
        return date.toISOString().split('T')[0];
    }
}
