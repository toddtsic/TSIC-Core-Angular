import { Component, ChangeDetectionStrategy, input, output, signal, computed, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AccountingRecordDto, CreditCardInfo, ClubTeamSummaryDto } from '@core/api';

type PaymentType = 'cc' | 'check' | 'correction';

/** Emitted when user confirms a CC charge. Parent handles the API call. */
export interface CcChargeEvent {
	creditCard: CreditCardInfo;
	amount: number;
}

/** Emitted when user submits a check or correction. Parent handles the API call. */
export interface CheckOrCorrectionEvent {
	amount: number;
	checkNo: string | null;
	comment: string | null;
	paymentType: 'Check' | 'Correction';
}


@Component({
	selector: 'app-accounting-ledger',
	standalone: true,
	imports: [CommonModule, FormsModule],
	templateUrl: './accounting-ledger.component.html',
	styleUrl: './accounting-ledger.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class AccountingLedgerComponent {

	@HostListener('document:click') onDocumentClick(): void { this.popoverAId.set(null); }
	@HostListener('document:keydown.escape') onEscapePopover(): void { this.popoverAId.set(null); }

	// ── Data inputs ──
	records = input<AccountingRecordDto[]>([]);
	feeTotal = input<number>(0);
	paidTotal = input<number>(0);
	owedTotal = input<number>(0);

	/** Club team breakdown for payment modal (teams only, optional) */
	clubBreakdown = input<ClubTeamSummaryDto[] | undefined>(undefined);

	// ── Outputs (callback pattern — parent handles API calls) ──
	refundRequested = output<AccountingRecordDto>();
	ccChargeSubmitted = output<CcChargeEvent>();
	checkSubmitted = output<CheckOrCorrectionEvent>();

	// ── Payment modal state ──
	showPaymentModal = signal(false);
	paymentType = signal<PaymentType>('check');
	amount = signal<number>(0);
	comment = signal('');
	checkNo = signal('');
	showCcConfirm = signal(false);

	// CC form fields
	ccNumber = signal('');
	ccExpiry = signal('');
	ccCvv = signal('');
	ccFirstName = signal('');
	ccLastName = signal('');
	ccAddress = signal('');
	ccZip = signal('');
	ccEmail = signal('');
	ccPhone = signal('');

	// ── Transaction detail popover ──
	popoverAId = signal<number | null>(null);

	togglePopover(record: AccountingRecordDto): void {
		this.popoverAId.set(this.popoverAId() === record.aId ? null : record.aId);
	}

	closePopover(): void {
		this.popoverAId.set(null);
	}

	/** Resolve team name from clubBreakdown by record's teamId. */
	teamNameFor(record: AccountingRecordDto): string | null {
		if (!record.teamId) return null;
		const teams = this.clubBreakdown();
		if (!teams) return null;
		const team = teams.find(t => t.teamId === record.teamId);
		if (!team) return null;
		return team.agegroupName ? `${team.agegroupName} · ${team.teamName}` : team.teamName;
	}

	/** True if this record has any detail worth showing in the popover. */
	hasDetails(record: AccountingRecordDto): boolean {
		return !!(record.adnTransactionId || record.adnInvoiceNo || record.checkNo
			|| record.adnCcExpDate || record.promoCode || record.comment);
	}

	/** True if the payment method is a credit card type. */
	isCcRecord(record: AccountingRecordDto): boolean {
		const m = (record.paymentMethod || '').toLowerCase();
		return m.includes('credit') || m.includes('card') || m.includes('cc');
	}

	/** True if the payment method is a check. */
	isCheckRecord(record: AccountingRecordDto): boolean {
		return (record.paymentMethod || '').toLowerCase().includes('check');
	}

	// ── Transaction table ──

	onRefundClick(record: AccountingRecordDto): void {
		this.refundRequested.emit(record);
	}

	// ── Payment modal ──

	/** Club balance due for check/correction: owed minus proportional fee reduction */
	checkBalanceDue = computed(() => {
		const breakdown = this.clubBreakdown();
		if (!breakdown?.length) return this.owedTotal();
		return breakdown
			.filter(t => t.owedTotal > 0)
			.reduce((sum, t) => sum + (t.owedTotal - (t.checkFeeReduction ?? 0)), 0);
	});

	/** Total proportional processing fee reduction when paying by check */
	totalFeeReduction = computed(() => {
		const breakdown = this.clubBreakdown();
		if (!breakdown?.length) return 0;
		return breakdown
			.filter(t => t.owedTotal > 0)
			.reduce((sum, t) => sum + (t.checkFeeReduction ?? 0), 0);
	});

	openPaymentModal(): void {
		this.paymentType.set('check');
		this.amount.set(this.checkBalanceDue());
		this.comment.set('');
		this.checkNo.set('');
		this.showCcConfirm.set(false);
		this.ccNumber.set('');
		this.ccExpiry.set('');
		this.ccCvv.set('');
		this.ccFirstName.set('');
		this.ccLastName.set('');
		this.ccAddress.set('');
		this.ccZip.set('');
		this.ccEmail.set('');
		this.ccPhone.set('');
		this.showPaymentModal.set(true);
	}

	closePaymentModal(): void {
		this.showPaymentModal.set(false);
	}

	/** Restrict amount to 2 decimal places */
	setAmount(value: number): void {
		this.amount.set(Math.round((value ?? 0) * 100) / 100);
	}

	selectPaymentType(type: PaymentType): void {
		this.paymentType.set(type);
		// CC charges full owed; check/correction uses adjusted balance (minus processing fees)
		this.amount.set(type === 'cc' ? this.owedTotal() : this.checkBalanceDue());
	}

	submitPayment(): void {
		if (this.paymentType() === 'cc') {
			this.showCcConfirm.set(true);
		} else {
			this.executePaymentSubmit();
		}
	}

	confirmCcCharge(): void {
		this.showCcConfirm.set(false);
		this.executePaymentSubmit();
	}

	dismissCcConfirm(): void {
		this.showCcConfirm.set(false);
	}

	ccLast4(): string {
		const num = this.ccNumber();
		return num.length >= 4 ? num.slice(-4) : num;
	}

	canSubmitPayment(): boolean {
		const type = this.paymentType();
		const amt = this.amount();

		if (type === 'cc') {
			return amt > 0 && amt <= this.owedTotal()
				&& !!this.ccNumber() && !!this.ccExpiry() && !!this.ccCvv()
				&& !!this.ccFirstName() && !!this.ccLastName();
		}
		if (type === 'check') {
			return amt > 0;
		}
		return amt !== 0;
	}

	// ── CC formatting ──

	formatCcNumber(value: string): void {
		this.ccNumber.set(value.replace(/\D/g, '').slice(0, 16));
	}

	formatExpiry(value: string): void {
		const digits = value.replace(/\D/g, '').slice(0, 4);
		this.ccExpiry.set(digits.length > 2 ? digits.slice(0, 2) + ' / ' + digits.slice(2) : digits);
	}

	formatCvv(value: string): void {
		this.ccCvv.set(value.replace(/\D/g, '').slice(0, 4));
	}

	formatPhone(value: string): void {
		this.ccPhone.set(value.replace(/\D/g, '').slice(0, 15));
	}

	// ── Private ──

	private executePaymentSubmit(): void {
		const type = this.paymentType();
		const amt = this.amount();

		if (type === 'cc') {
			const expiryRaw = this.ccExpiry().replace(/\D/g, '');
			this.ccChargeSubmitted.emit({
				creditCard: {
					number: this.ccNumber(),
					expiry: expiryRaw,
					code: this.ccCvv(),
					firstName: this.ccFirstName(),
					lastName: this.ccLastName(),
					address: this.ccAddress() || null,
					zip: this.ccZip() || null,
					email: this.ccEmail() || null,
					phone: this.ccPhone() || null
				},
				amount: amt
			});
		} else {
			this.checkSubmitted.emit({
				amount: amt,
				checkNo: this.checkNo() || null,
				comment: this.comment() || null,
				paymentType: type === 'check' ? 'Check' : 'Correction'
			});
		}

		this.closePaymentModal();
	}
}
