import { ChangeDetectionStrategy, Component, EventEmitter, Output, OnInit, inject, signal, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { CustomerConfigureService } from '../customer-configure.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import type { TimezoneDto, CreateCustomerRequest, UpdateCustomerRequest } from '@core/api';

export type DialogMode = 'add' | 'edit';

@Component({
    selector: 'customer-dialog',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './customer-dialog.component.html',
    styleUrl: './customer-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomerDialogComponent implements OnInit {
    readonly mode = input<DialogMode>('add');
    readonly customerId = input<string | null>(null);
    readonly timezones = input<TimezoneDto[]>([]);
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<void>();

    private readonly svc = inject(CustomerConfigureService);
    private readonly toast = inject(ToastService);

    // Form fields
    customerName = signal('');
    tzId = signal<number | null>(null);
    adnLoginId = signal('');
    adnTransactionKey = signal('');

    // UI state
    isLoadingDetail = signal(false);
    isSaving = signal(false);

    ngOnInit(): void {
        const customerId = this.customerId();
        if (this.mode() === 'edit' && customerId) {
            this.isLoadingDetail.set(true);
            this.svc.getById(customerId).subscribe({
                next: (detail) => {
                    this.customerName.set(detail.customerName ?? '');
                    this.tzId.set(detail.tzId);
                    this.adnLoginId.set(detail.adnLoginId ?? '');
                    this.adnTransactionKey.set(detail.adnTransactionKey ?? '');
                    this.isLoadingDetail.set(false);
                },
                error: () => {
                    this.toast.show('Failed to load customer detail', 'danger');
                    this.close.emit();
                }
            });
        }
    }

    onInput(field: 'customerName' | 'adnLoginId' | 'adnTransactionKey', event: Event): void {
        const value = (event.target as HTMLInputElement).value;
        this[field].set(value);
    }

    onTzChange(event: Event): void {
        const value = (event.target as HTMLSelectElement).value;
        this.tzId.set(value ? +value : null);
    }

    isValid(): boolean {
        return this.customerName().trim().length > 0 && this.tzId() !== null;
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;
        this.isSaving.set(true);

        const customerId = this.customerId();
        if (this.mode() === 'add') {
            const request: CreateCustomerRequest = {
                customerName: this.customerName().trim(),
                tzId: this.tzId()!,
                adnLoginId: this.adnLoginId() || undefined,
                adnTransactionKey: this.adnTransactionKey() || undefined
            };

            this.svc.create(request).subscribe({
                next: () => {
                    this.toast.show(`Customer "${this.customerName().trim()}" created`, 'success');
                    this.saved.emit();
                },
                error: (err) => {
                    this.toast.show(err.error?.message || 'Failed to create customer', 'danger');
                    this.isSaving.set(false);
                }
            });
        } else if (customerId) {
            const request: UpdateCustomerRequest = {
                customerName: this.customerName().trim(),
                tzId: this.tzId()!,
                adnLoginId: this.adnLoginId() || undefined,
                adnTransactionKey: this.adnTransactionKey() || undefined
            };

            this.svc.update(customerId, request).subscribe({
                next: () => {
                    this.toast.show(`Customer "${this.customerName().trim()}" updated`, 'success');
                    this.saved.emit();
                },
                error: (err) => {
                    this.toast.show(err.error?.message || 'Failed to update customer', 'danger');
                    this.isSaving.set(false);
                }
            });
        }
    }
}
