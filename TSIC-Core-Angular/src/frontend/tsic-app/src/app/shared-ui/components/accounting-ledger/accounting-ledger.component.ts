import { Component, ChangeDetectionStrategy, input, output, signal, computed, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AccountingRecordDto, CreditCardInfo, RegisteredTeamDto } from '@core/api';

type PaymentType = 'cc' | 'check' | 'correction' | 'refund';

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

/** Emitted when user confirms a CC refund. Parent handles the API call. */
export interface RefundEvent {
	accountingRecordId: number;
	refundAmount: number;
}

/**
 * Neutral grouping unit for bucketing + per-row attribution in the ledger. The club-rep
 * path groups by team; the family path groups by child player. `key` matches a record's
 * discriminator (teamId or ownerRegistrationId); `active` drives the active-vs-other split.
 */
export interface LedgerGroup {
	key: string;
	label: string;
	active: boolean;
}

/**
 * One choosable registration for a new accounting record. When a caller supplies more than one
 * (a player signed up for several events), the "Add Accounting Record" modal opens on a
 * "which registration?" step before the payment form. Each target carries its OWN balance
 * figures so the modal's amount caps (check/correction/CC) bound to the picked registration —
 * not the player's combined total. A record-less registration is still a valid target (this is
 * exactly the gap the old in-ledger row-click couldn't reach).
 */
export interface LedgerAddTarget {
	key: string;        // the owning registrationId
	label: string;      // "AgeGroup · Team", or a date fallback when no records carry a label
	owed: number;       // CC-side owed (gross)
	checkOwed: number;  // check/correction owed (processing fees removed)
	paid: number;       // amount already paid (bounds a negative correction)
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

	/** Scope-level check/correction owed (CkOwedTotal, summed by the parent). When
	 *  omitted, check/correction default to full owedTotal — used by callers with no
	 *  per-method breakdown (e.g. individual registrations). */
	checkOwed = input<number | undefined>(undefined);

	/** Club team breakdown for the payment modal's distribution previews (teams only, optional) */
	clubBreakdown = input<RegisteredTeamDto[] | undefined>(undefined);

	/** Explicit grouping for bucketing/labeling (family path). When omitted, groups are
	 *  derived from clubBreakdown (team path). */
	groups = input<LedgerGroup[] | undefined>(undefined);

	/** Heading for the "other" (excluded-from-active) bucket and the refund modal's group row. */
	otherBucketLabel = input<string>('Waitlisted / Dropped / Inactive');
	groupHeading = input<string>('Team');

	/** Shows the "+ Add Accounting Record" button. Off for the aggregated family scope,
	 *  whose family-wide charge is a fast-follow; per-row refunds remain available. */
	allowAdd = input<boolean>(true);

	/** Registrations a new record can attach to. Empty / single → no picker (the modal opens
	 *  straight to the form, using the input balances). More than one → the modal first asks
	 *  which registration, then bounds its amounts to that target. The family ledger supplies one
	 *  per event so a multi-event player can record against any event, including a record-less one. */
	addTargets = input<LedgerAddTarget[]>([]);

	/** Unified grouping source: explicit groups, else derived from the team breakdown,
	 *  else none. Keeps the club-rep caller unchanged (it still passes clubBreakdown only). */
	private effectiveGroups = computed<LedgerGroup[]>(() => {
		const g = this.groups();
		if (g) return g;
		const cb = this.clubBreakdown();
		if (cb) return cb.map(t => ({
			key: t.teamId,
			label: t.ageGroupName ? `${t.ageGroupName} · ${t.teamName}` : t.teamName,
			active: t.active
				&& !t.ageGroupName.toUpperCase().startsWith('WAITLIST')
				&& !t.ageGroupName.toUpperCase().startsWith('DROPPED')
		}));
		return [];
	});

	/** A record's group discriminator. The family path (explicit groups) keys by the owning
	 *  child; the team path keys by team. Selecting by path avoids a stray teamId on a player
	 *  record shadowing its ownerRegistrationId. */
	private recordKey(r: AccountingRecordDto): string | null {
		return this.groups() ? (r.ownerRegistrationId ?? null) : (r.teamId ?? null);
	}

	/** Group keys excluded from the active bucket (waitlist/dropped/inactive / inactive child). */
	private otherGroupKeys = computed(() =>
		new Set(this.effectiveGroups().filter(g => !g.active).map(g => g.key)));

	/** Active records */
	activeRecords = computed(() => {
		const other = this.otherGroupKeys();
		if (other.size === 0) return this.records();
		return this.records().filter(r => { const k = this.recordKey(r); return !k || !other.has(k); });
	});

	/** Records belonging to excluded groups */
	otherRecords = computed(() => {
		const other = this.otherGroupKeys();
		if (other.size === 0) return [];
		return this.records().filter(r => { const k = this.recordKey(r); return k != null && other.has(k); });
	});

	// ── Outputs (callback pattern — parent handles API calls) ──
	ccChargeSubmitted = output<CcChargeEvent>();
	checkSubmitted = output<CheckOrCorrectionEvent>();
	refundSubmitted = output<RefundEvent>();
	/** The registration a new record is being recorded against (the picked add-target's key).
	 *  Parent points its payment/charge call at this registration. */
	addTargetSelected = output<string>();

	// ── Payment modal state ──
	showPaymentModal = signal(false);
	paymentType = signal<PaymentType>('check');

	// ── Add-target picker state ──
	// When the modal opens with >1 addTargets, it first shows a "which registration?" step
	// (pickingTarget). Once chosen, the picked target drives the modal's balance figures so the
	// amount caps bound to that one registration. Null target = no per-registration override
	// (single/no addTargets — the modal uses the input balances, as the single-team ledger does).
	pickingTarget = signal(false);
	selectedAddTarget = signal<LedgerAddTarget | null>(null);

	/** Owed used by the modal — the picked add-target's, else the scope input. */
	modalOwed = computed(() => this.selectedAddTarget()?.owed ?? this.owedTotal());
	/** Paid used by the modal — the picked add-target's, else the scope input. */
	modalPaid = computed(() => this.selectedAddTarget()?.paid ?? this.paidTotal());

	// ── Refund mode state ──
	refundRecord = signal<AccountingRecordDto | null>(null);
	showRefundConfirm = signal(false);
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

	/** Resolve a record's group label (team name, or family player name). */
	teamNameFor(record: AccountingRecordDto): string | null {
		const k = this.recordKey(record);
		if (!k) return null;
		return this.effectiveGroups().find(g => g.key === k)?.label ?? null;
	}

	/** The owning player's assigned team for a record (family path) — "AgeGroup · TeamName".
	 *  Lets a director tell which team a transaction belongs to when a parent registered
	 *  several players. Null when the record carries no assigned-team stamp (single-player /
	 *  club-rep paths, or a player not yet on a team). */
	ownerTeamLabel(record: AccountingRecordDto): string | null {
		const team = record.ownerTeamName?.trim();
		if (!team) return null;
		const ageGroup = record.ownerAgeGroupName?.trim();
		return ageGroup ? `${ageGroup} · ${team}` : team;
	}

	/** True when the comment is the system-generated charge description, which embeds the
	 *  player name as a colon-delimited segment ("{Job}:{Player}:{AgeGroup}:{Team}" with a
	 *  team, or "{Role}:{Player}" without). Fully redundant in the family ledger now that the
	 *  row shows the owning player and assigned team — and the leading job name is noise in
	 *  this job-scoped panel — so it's suppressed. Keyed off the player name (not the team /
	 *  agegroup) because those can be renamed after payment: the stored description keeps the
	 *  old name, so a team/agegroup match is brittle. Genuine manual comments don't carry the
	 *  ":Player" segment and still show. */
	isAutoChargeDescription(record: AccountingRecordDto): boolean {
		const comment = record.comment?.trim();
		const owner = record.ownerName?.trim();
		if (!comment || !owner) return false;
		return comment.includes(`:${owner}`);
	}

	/** Comment to display — null when it's the redundant auto charge description. */
	displayComment(record: AccountingRecordDto): string | null {
		return this.isAutoChargeDescription(record) ? null : (record.comment ?? null);
	}

	/** True if this record has any detail worth showing in the popover. */
	hasDetails(record: AccountingRecordDto): boolean {
		return !!(record.adnTransactionId || record.adnInvoiceNo || record.checkNo
			|| record.adnCcExpDate || record.promoCode || this.displayComment(record));
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
		this.refundRecord.set(record);
		this.paymentType.set('refund');
		this.amount.set(record.paidAmount ?? 0);
		this.comment.set('');
		this.showCcConfirm.set(false);
		this.showPaymentModal.set(true);
	}

	// ── Payment modal ──

	/** Balance due for check/correction — the picked add-target's check owed, else the scope's
	 *  canonical check owed (CkOwedTotal, summed by the parent via PaymentState.ResolveOwed).
	 *  Falls back to full owed when no checkOwed is supplied. */
	checkBalanceDue = computed(() => this.selectedAddTarget()?.checkOwed ?? this.checkOwed() ?? this.modalOwed());

	/** Processing fees removed by paying via check/correction = CC owed − check owed. */
	totalFeeReduction = computed(() => Math.max(0, this.modalOwed() - this.checkBalanceDue()));

	/** True when typed check amount exceeds the canonical balance due — drives the
	 *  inline error and disables Submit. Corrections are intentional ± adjustments
	 *  and are excluded from this guard. */
	checkExceedsBalance = computed(() =>
		this.paymentType() === 'check' && this.amount() > this.checkBalanceDue()
	);

	/** Correction bounds — functionally a two-way check: a positive correction can't
	 *  "pay" more than the balance due (same cap as a check — CkOwedTotal, processing
	 *  fees removed), and a negative correction can't "refund" more than they've paid.
	 *  Upper = checkBalanceDue, lower = -PaidTotal. */
	correctionExceedsBounds = computed(() => {
		if (this.paymentType() !== 'correction') return false;
		const amt = this.amount();
		return amt > this.checkBalanceDue() || amt < -this.modalPaid();
	});

	/** Add-record entry point. With more than one target, ask which registration first; otherwise
	 *  go straight to the form (auto-selecting the sole target so its balances bound the amounts).
	 *  Zero targets = no per-registration override (single-team / single-registration callers). */
	openPaymentModal(): void {
		const targets = this.addTargets();
		this.clearPaymentForm();
		if (targets.length > 1) {
			this.selectedAddTarget.set(null);
			this.pickingTarget.set(true);
			this.showPaymentModal.set(true);
			return;
		}
		const single = targets.length === 1 ? targets[0] : null;
		this.selectedAddTarget.set(single);
		if (single) this.addTargetSelected.emit(single.key);
		this.pickingTarget.set(false);
		this.beginNormalEntry();
		this.showPaymentModal.set(true);
	}

	/** Pick which registration a new record applies to, then advance to the form. The target's
	 *  balances now bound the amount caps (checkBalanceDue / modalOwed / modalPaid). */
	chooseAddTarget(target: LedgerAddTarget): void {
		this.selectedAddTarget.set(target);
		this.addTargetSelected.emit(target.key);
		this.pickingTarget.set(false);
		this.beginNormalEntry();
	}

	/** Re-open the "which registration?" step from the form (the "Change" affordance). */
	changeAddTarget(): void {
		this.pickingTarget.set(true);
	}

	/** Seed the form defaults once a target is settled (or none is needed). */
	private beginNormalEntry(): void {
		this.paymentType.set('check');
		this.amount.set(this.checkBalanceDue());
	}

	/** Clear all entry fields (called before either the picker or the form is shown). */
	private clearPaymentForm(): void {
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
	}

	closePaymentModal(): void {
		this.showPaymentModal.set(false);
		this.refundRecord.set(null);
		this.showRefundConfirm.set(false);
		this.pickingTarget.set(false);
		this.selectedAddTarget.set(null);
	}

	/** Restrict amount to 2 decimal places */
	setAmount(value: number): void {
		this.amount.set(Math.round((value ?? 0) * 100) / 100);
	}

	selectPaymentType(type: PaymentType): void {
		this.paymentType.set(type);
		// CC charges full owed; check/correction uses adjusted balance (minus processing fees)
		this.amount.set(type === 'cc' ? this.modalOwed() : this.checkBalanceDue());
	}

	submitPayment(): void {
		if (this.paymentType() === 'refund') {
			this.showRefundConfirm.set(true);
		} else if (this.paymentType() === 'cc') {
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

	confirmRefund(): void {
		this.showRefundConfirm.set(false);
		this.executePaymentSubmit();
	}

	dismissRefundConfirm(): void {
		this.showRefundConfirm.set(false);
	}

	ccLast4(): string {
		const num = this.ccNumber();
		return num.length >= 4 ? num.slice(-4) : num;
	}

	canSubmitPayment(): boolean {
		const type = this.paymentType();
		const amt = this.amount();

		if (type === 'cc') {
			return amt > 0 && amt <= this.modalOwed()
				&& !!this.ccNumber() && !!this.ccExpiry() && !!this.ccCvv()
				&& !!this.ccFirstName() && !!this.ccLastName();
		}
		if (type === 'check') {
			return amt > 0 && amt <= this.checkBalanceDue();
		}
		if (type === 'refund') {
			const maxRefund = this.refundRecord()?.paidAmount ?? 0;
			return amt > 0 && amt <= maxRefund;
		}
		if (type === 'correction') {
			return amt !== 0 && amt <= this.checkBalanceDue() && amt >= -this.modalPaid();
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

		if (type === 'refund') {
			const record = this.refundRecord();
			if (record?.aId) {
				this.refundSubmitted.emit({
					accountingRecordId: record.aId,
					refundAmount: amt
				});
			}
		} else if (type === 'cc') {
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
