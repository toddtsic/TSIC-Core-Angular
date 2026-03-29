import { Component, ChangeDetectionStrategy, signal, input, output, inject, linkedSignal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, CreditCardInfo } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';

type PaymentType = 'cc' | 'check' | 'correction';

@Component({
	selector: 'app-add-team-payment-modal',
	standalone: true,
	imports: [CommonModule, FormsModule],
	templateUrl: './add-team-payment-modal.component.html',
	styleUrl: './add-team-payment-modal.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class AddTeamPaymentModalComponent {
	private searchService = inject(TeamSearchService);
	private toast = inject(ToastService);

	detail = input<TeamSearchDetailDto | null>(null);
	scope = input<'team' | 'club'>('team');
	isOpen = input<boolean>(false);

	closed = output<void>();
	paymentRecorded = output<void>();

	paymentType = linkedSignal({ source: () => this.detail(), computation: () => 'check' as PaymentType });
	amount = linkedSignal(() => this.owedTotal);
	comment = linkedSignal({ source: () => this.detail(), computation: () => '' });
	checkNo = linkedSignal({ source: () => this.detail(), computation: () => '' });
	isProcessing = signal<boolean>(false);
	showConfirm = signal<boolean>(false);

	// CC fields — reset on new detail
	ccNumber = linkedSignal({ source: () => this.detail(), computation: () => '' });
	ccExpiry = linkedSignal({ source: () => this.detail(), computation: () => '' });
	ccCvv = linkedSignal({ source: () => this.detail(), computation: () => '' });
	ccFirstName = linkedSignal(() => this.detail()?.clubRepName?.split(', ').pop() ?? '');
	ccLastName = linkedSignal(() => this.detail()?.clubRepName?.split(', ').shift() ?? '');
	ccAddress = linkedSignal(() => this.detail()?.clubRepStreetAddress ?? '');
	ccZip = linkedSignal(() => this.detail()?.clubRepPostalCode ?? '');
	ccEmail = linkedSignal(() => this.detail()?.clubRepEmail ?? '');
	ccPhone = linkedSignal(() => this.detail()?.clubRepCellphone ?? '');

	get owedTotal(): number {
		if (this.scope() === 'club') {
			return this.detail()?.clubTeamSummaries?.reduce((s, t) => s + t.owedTotal, 0) ?? 0;
		}
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

	private submitCcCharge(d: TeamSearchDetailDto, amt: number): void {
		if (amt <= 0) { this.toast.show('Amount must be greater than zero', 'danger', 4000); return; }
		if (amt > this.owedTotal) { this.toast.show('Amount cannot exceed owed total', 'danger', 4000); return; }
		if (!d.clubRepRegistrationId) { this.toast.show('No club rep registration found', 'danger', 4000); return; }

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

		const request = {
			clubRepRegistrationId: d.clubRepRegistrationId,
			creditCard
		};

		this.isProcessing.set(true);
		const call = this.scope() === 'team'
			? this.searchService.chargeCcForTeam(d.teamId, request)
			: this.searchService.chargeCcForClub(d.clubRepRegistrationId, request);

		call.subscribe({
			next: (result) => {
				this.isProcessing.set(false);
				if (result.success) {
					this.toast.show(`CC charge successful: $${amt.toFixed(2)}`, 'success', 3000);
					this.paymentRecorded.emit();
					this.close();
				} else {
					this.toast.show(`CC charge failed: ${result.error || 'Unknown error'}`, 'danger', 5000);
				}
			},
			error: (err) => {
				this.isProcessing.set(false);
				this.toast.show(`CC charge failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	private submitCheckOrCorrection(d: TeamSearchDetailDto, amt: number, type: 'check' | 'correction'): void {
		if (type === 'check' && amt <= 0) { this.toast.show('Check amount must be greater than zero', 'danger', 4000); return; }
		if (type === 'correction' && amt === 0) { this.toast.show('Correction amount cannot be zero', 'danger', 4000); return; }
		if (!d.clubRepRegistrationId) { this.toast.show('No club rep registration found', 'danger', 4000); return; }

		const paymentType = type === 'check' ? 'Check' : 'Correction';

		const request = {
			clubRepRegistrationId: d.clubRepRegistrationId,
			amount: amt,
			checkNo: this.checkNo() || undefined,
			comment: this.comment() || undefined,
			paymentType
		};

		this.isProcessing.set(true);
		const call = this.scope() === 'team'
			? this.searchService.recordCheckForTeam(d.teamId, request)
			: this.searchService.recordCheckForClub(d.clubRepRegistrationId, request);

		call.subscribe({
			next: (result) => {
				this.isProcessing.set(false);
				if (result.success) {
					this.toast.show(`${paymentType} recorded: $${amt.toFixed(2)}`, 'success', 3000);
					this.paymentRecorded.emit();
					this.close();
				} else {
					this.toast.show(`Failed: ${result.error || 'Unknown error'}`, 'danger', 5000);
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
