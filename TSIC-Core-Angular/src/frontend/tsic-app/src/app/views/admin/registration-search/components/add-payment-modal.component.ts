import { Component, ChangeDetectionStrategy, signal, input, output, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { RegistrationDetailDto, CreditCardInfo } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

type PaymentType = 'cc' | 'check' | 'correction';

@Component({
  selector: 'app-add-payment-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './add-payment-modal.component.html',
  styleUrl: './add-payment-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AddPaymentModalComponent {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  detail = input<RegistrationDetailDto | null>(null);
  isOpen = input<boolean>(false);

  closed = output<void>();
  paymentRecorded = output<void>();

  paymentType = signal<PaymentType>('check');
  amount = signal<number>(0);
  comment = signal<string>('');
  checkNo = signal<string>('');
  isProcessing = signal<boolean>(false);
  showConfirm = signal<boolean>(false);

  // CC fields
  ccNumber = signal<string>('');
  ccExpiry = signal<string>('');
  ccCvv = signal<string>('');
  ccFirstName = signal<string>('');
  ccLastName = signal<string>('');
  ccAddress = signal<string>('');
  ccZip = signal<string>('');
  ccEmail = signal<string>('');
  ccPhone = signal<string>('');

  constructor() {
    effect(() => {
      const d = this.detail();
      if (d && this.isOpen()) {
        this.amount.set(d.owedTotal ?? 0);
        // Pre-fill CC name from registration, contact from family or demographics
        this.ccFirstName.set(d.firstName || '');
        this.ccLastName.set(d.lastName || '');
        if (d.familyContact) {
          this.ccEmail.set(d.familyContact.momEmail || d.familyContact.dadEmail || d.email || '');
          this.ccPhone.set(d.familyContact.momCellphone || d.familyContact.dadCellphone || '');
        } else if (d.userDemographics) {
          this.ccEmail.set(d.userDemographics.email || d.email || '');
          this.ccPhone.set(d.userDemographics.cellphone || '');
          this.ccAddress.set(d.userDemographics.streetAddress || '');
          this.ccZip.set(d.userDemographics.postalCode || '');
        }
      }
    });
  }

  get owedTotal(): number {
    return this.detail()?.owedTotal ?? 0;
  }

  close(): void {
    this.closed.emit();
    this.resetForm();
  }

  selectType(type: PaymentType): void {
    this.paymentType.set(type);
  }

  submit(): void {
    if (!this.detail()) return;

    if (this.paymentType() === 'cc') {
      // CC charges require confirmation before processing
      this.showConfirm.set(true);
    } else {
      this.executeSubmit();
    }
  }

  confirmSubmit(): void {
    this.showConfirm.set(false);
    this.executeSubmit();
  }

  dismissConfirm(): void {
    this.showConfirm.set(false);
  }

  private executeSubmit(): void {
    const d = this.detail();
    if (!d) return;

    const type = this.paymentType();
    const amt = this.amount();

    if (type === 'cc') {
      this.submitCcCharge(d, amt);
    } else {
      this.submitCheckOrCorrection(d, amt, type);
    }
  }

  ccLast4(): string {
    const num = this.ccNumber();
    return num.length >= 4 ? num.slice(-4) : num;
  }

  canSubmit(): boolean {
    const type = this.paymentType();
    const amt = this.amount();
    if (this.isProcessing()) return false;

    if (type === 'cc') {
      return amt > 0 && amt <= this.owedTotal
        && !!this.ccNumber() && !!this.ccExpiry() && !!this.ccCvv()
        && !!this.ccFirstName() && !!this.ccLastName();
    }
    if (type === 'check') {
      return amt > 0;
    }
    // correction: any non-zero amount
    return amt !== 0;
  }

  // ── CC formatting ──

  formatCcNumber(value: string): void {
    this.ccNumber.set(value.replace(/\D/g, '').slice(0, 16));
  }

  formatExpiry(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 4);
    if (digits.length > 2) {
      this.ccExpiry.set(digits.slice(0, 2) + ' / ' + digits.slice(2));
    } else {
      this.ccExpiry.set(digits);
    }
  }

  formatCvv(value: string): void {
    this.ccCvv.set(value.replace(/\D/g, '').slice(0, 4));
  }

  formatPhone(value: string): void {
    this.ccPhone.set(value.replace(/\D/g, '').slice(0, 15));
  }

  // ── Private ──

  private submitCcCharge(d: RegistrationDetailDto, amt: number): void {
    if (amt <= 0) { this.toast.show('Amount must be greater than zero', 'danger', 4000); return; }
    if (amt > this.owedTotal) { this.toast.show('Amount cannot exceed owed total', 'danger', 4000); return; }

    // Build expiry in MMYY format for backend
    const expiryRaw = this.ccExpiry().replace(/\D/g, '');

    const creditCard: CreditCardInfo = {
      number: this.ccNumber(),
      expiry: expiryRaw,
      code: this.ccCvv(),
      firstName: this.ccFirstName(),
      lastName: this.ccLastName(),
      address: this.ccAddress() || null,
      zip: this.ccZip() || null,
      email: this.ccEmail() || null,
      phone: this.ccPhone() || null
    };

    this.isProcessing.set(true);
    this.searchService.chargeCc(d.registrationId, {
      registrationId: d.registrationId,
      creditCard: creditCard,
      amount: amt
    }).subscribe({
      next: (response) => {
        this.isProcessing.set(false);
        if (response.success) {
          this.toast.show(`CC charge successful: $${amt.toFixed(2)}`, 'success', 3000);
          this.paymentRecorded.emit();
          this.close();
        } else {
          this.toast.show(`CC charge failed: ${response.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => {
        this.isProcessing.set(false);
        this.toast.show(`CC charge failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000);
      }
    });
  }

  private submitCheckOrCorrection(d: RegistrationDetailDto, amt: number, type: 'check' | 'correction'): void {
    if (type === 'check' && amt <= 0) { this.toast.show('Check amount must be greater than zero', 'danger', 4000); return; }
    if (type === 'correction' && amt === 0) { this.toast.show('Correction amount cannot be zero', 'danger', 4000); return; }

    const paymentType = type === 'check' ? 'Check' : 'Correction';

    this.isProcessing.set(true);
    this.searchService.recordPayment(d.registrationId, {
      registrationId: d.registrationId,
      amount: amt,
      paymentType: paymentType,
      checkNo: this.checkNo() || null,
      comment: this.comment() || null
    }).subscribe({
      next: (response) => {
        this.isProcessing.set(false);
        if (response.success) {
          this.toast.show(`${paymentType} recorded: $${amt.toFixed(2)}`, 'success', 3000);
          this.paymentRecorded.emit();
          this.close();
        } else {
          this.toast.show(`Failed: ${response.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => {
        this.isProcessing.set(false);
        this.toast.show(`Failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000);
      }
    });
  }

  private resetForm(): void {
    this.paymentType.set('check');
    this.amount.set(0);
    this.comment.set('');
    this.checkNo.set('');
    this.isProcessing.set(false);
    this.showConfirm.set(false);
    this.ccNumber.set('');
    this.ccExpiry.set('');
    this.ccCvv.set('');
    this.ccFirstName.set('');
    this.ccLastName.set('');
    this.ccAddress.set('');
    this.ccZip.set('');
    this.ccEmail.set('');
    this.ccPhone.set('');
  }
}
