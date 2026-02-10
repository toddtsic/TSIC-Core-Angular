import { Component, ChangeDetectionStrategy, signal, input, output, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AccountingRecordDto, RefundResponse } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

@Component({
  selector: 'app-refund-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './refund-modal.component.html',
  styleUrl: './refund-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RefundModalComponent {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  accountingRecord = input<AccountingRecordDto | null>(null);
  isOpen = input<boolean>(false);

  closed = output<void>();
  refunded = output<RefundResponse>();

  refundAmount = signal<number>(0);
  reason = signal<string>('');
  isProcessing = signal<boolean>(false);

  constructor() {
    effect(() => {
      const record = this.accountingRecord();
      if (record) { this.refundAmount.set(record.paidAmount ?? 0); }
    });
  }

  close(): void { this.closed.emit(); this.resetForm(); }

  processRefund(): void {
    const record = this.accountingRecord();
    if (!record) return;
    const amount = this.refundAmount();
    const reasonText = this.reason();
    if (amount <= 0) { this.toast.show('Refund amount must be greater than zero', 'danger', 4000); return; }
    if (amount > (record.paidAmount ?? 0)) { this.toast.show('Refund amount cannot exceed original payment amount', 'danger', 4000); return; }
    if (!reasonText.trim()) { this.toast.show('Refund reason is required', 'danger', 4000); return; }

    this.isProcessing.set(true);
    this.searchService.processRefund({
      accountingRecordId: record.aId,
      refundAmount: amount,
      reason: reasonText
    }).subscribe({
      next: (response) => {
        this.isProcessing.set(false);
        this.toast.show(`Refund processed successfully: $${amount.toFixed(2)}`, 'success', 3000);
        this.refunded.emit(response);
        this.close();
      },
      error: (err) => {
        this.isProcessing.set(false);
        this.toast.show(`Refund failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
      }
    });
  }

  private resetForm(): void { this.refundAmount.set(0); this.reason.set(''); this.isProcessing.set(false); }
}
