import { Component, ChangeDetectionStrategy, input, output, signal, computed, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AccountingRecordDto, CreditCardInfo, RegisteredTeamDto } from '@core/api';
import { DraggableModalDirective } from '../../directives/draggable-modal.directive';

type PaymentType = 'cc' | 'check' | 'correction' | 'refund';

/** Sortable ledger columns. `team` is the first cell (it leads with the team/owner line) and
 *  stays the default, so the ledger opens exactly as it does today. */
export type LedgerSortColumn = 'team' | 'date' | 'due' | 'paid';
export type LedgerSortDir = 'asc' | 'desc';

/** Emitted when user confirms a CC charge. Parent handles the API call. */
export interface CcChargeEvent {
	creditCard: CreditCardInfo;
	amount: number;
	comment: string | null;
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
	/** Director's typed reason (PL-058). Null when left blank — hosts fall back to their
	 *  legacy hardcoded reason so an empty field reproduces the old behavior exactly. */
	comment: string | null;
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
	/** True when THIS registration is on a live Authorize.Net plan (AR-032). Per-target, not
	 *  per-scope: in the family ledger one sibling can be on a plan while another is not. */
	arbLive?: boolean;
}


@Component({
	selector: 'app-accounting-ledger',
	standalone: true,
	imports: [CommonModule, FormsModule, DraggableModalDirective],
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

	/** Shows the active/inactive activation hints in the check & correction forms ("…becomes
	 *  Active", "…you may want to toggle them Active"). On for player/registration scopes, whose
	 *  registrations start Inactive until paid. Off for the team scope — teams are created Active
	 *  at registration, so the hints are irrelevant and misleading. */
	showActivationNotes = input<boolean>(true);

	/** Registrations a new record can attach to. Empty / single → no picker (the modal opens
	 *  straight to the form, using the input balances). More than one → the modal first asks
	 *  which registration, then bounds its amounts to that target. The family ledger supplies one
	 *  per event so a multi-event player can record against any event, including a record-less one. */
	addTargets = input<LedgerAddTarget[]>([]);

	/** Negative corrections (claw-backs — raise the amount owed) are allowed wherever the
	 *  record lands on ONE known target: single registrations, family targets, single teams.
	 *  The club-rep caller turns this off at CLUB scope, where a new record distributes across
	 *  teams and a negative has no sensible auto-attribution — the server rejects it too; this
	 *  input just keeps the admin from composing a request that would bounce. */
	allowNegativeCorrection = input<boolean>(true);

	/** Scope-level ARB liveness, for callers with no add-target list (the single-registration
	 *  detail panel). Ignored once a target is picked — the target's own flag wins. */
	arbLive = input<boolean>(false);

	/** Whether the signed-in role may still enter a Correction against a LIVE plan. Superuser
	 *  only (AR-032) — Director and SuperDirector are locked out. The lock is a courtesy that
	 *  explains itself; the real control is the server-side gate in RecordCheckOrCorrectionAsync. */
	canCorrectLivePlan = input<boolean>(false);

	/** Job CC processing rate as a MULTIPLIER (e.g. 0.035; 0 = proc disabled), from the
	 *  host's DTO (`ccProcRate`). Authoritative source for the correction impact note and
	 *  the net-adjustment solver — with it supplied, both are exact even on a settled
	 *  balance. When omitted, the modal falls back to deriving the rate from its balances. */
	procRate = input<number | undefined>(undefined);

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

	/** Sort key that groups ledger rows by the team a transaction involves. Prefers the
	 *  record's own assigned-team stamp (family / single-registration paths), falling back to
	 *  its group label (the club-rep path keys rows by team). Unattributed rows sort first. */
	private teamSortKey(r: AccountingRecordDto): string {
		return (this.ownerTeamLabel(r) ?? this.teamNameFor(r) ?? '').toLowerCase();
	}

	// ── Column sorting (AR-023) ──
	// ONE sort key at a time, the model every grid user already has. Team is simply the
	// column that happens to be the default, so clicking any other heading dissolves the
	// team grouping and clicking Transaction brings it back. The rejected alternative —
	// team always primary, chosen column secondary — keeps the grouping but turns "sort by
	// date" into N chronological runs a director has to scan, which defeats the ask.
	// Ties keep incoming order (Array.sort is stable), so team/asc reproduces today's
	// rendering byte for byte and nothing moves until a heading is clicked.

	/** First click on a heading uses the direction a person actually wants: newest money and
	 *  biggest amounts first, but teams A→Z. */
	private static readonly SORT_DEFAULT_DIR: Record<LedgerSortColumn, LedgerSortDir> = {
		team: 'asc', date: 'desc', due: 'desc', paid: 'desc'
	};

	sortColumn = signal<LedgerSortColumn>('team');
	sortDir = signal<LedgerSortDir>('asc');

	/** Click a heading: same column flips direction, a new column adopts its natural default. */
	toggleSort(col: LedgerSortColumn): void {
		if (this.sortColumn() === col) {
			this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
			return;
		}
		this.sortColumn.set(col);
		this.sortDir.set(AccountingLedgerComponent.SORT_DEFAULT_DIR[col]);
	}

	/** `aria-sort` for a heading — 'none' unless it is the active column. */
	ariaSort(col: LedgerSortColumn): 'ascending' | 'descending' | 'none' {
		if (this.sortColumn() !== col) return 'none';
		return this.sortDir() === 'asc' ? 'ascending' : 'descending';
	}

	/** Caret class for a heading; the inactive carets stay in the DOM (dimmed by CSS) so the
	 *  headings do not reflow by a few px on every click. */
	sortIcon(col: LedgerSortColumn): string {
		if (this.sortColumn() !== col) return 'bi-arrow-down-up';
		return this.sortDir() === 'asc' ? 'bi-sort-down-alt' : 'bi-sort-down';
	}

	/** Epoch for a record's date. Unparseable/absent dates sort last in both directions. */
	private dateValue(r: AccountingRecordDto): number {
		const t = r.date ? Date.parse(r.date) : NaN;
		return Number.isNaN(t) ? Number.NEGATIVE_INFINITY : t;
	}

	/** Comparator for the active column, in ascending terms; direction is applied by the caller. */
	private compareBy(col: LedgerSortColumn, a: AccountingRecordDto, b: AccountingRecordDto): number {
		switch (col) {
			case 'date': return this.dateValue(a) - this.dateValue(b);
			// A null amount renders blank and orders as zero — it is an absent charge, not an
			// unknown one. (Distinct from the AR-011 `?? 0` on the amount INPUT; nothing here
			// writes a value back.)
			case 'due': return (a.dueAmount ?? 0) - (b.dueAmount ?? 0);
			case 'paid': return (a.paidAmount ?? 0) - (b.paidAmount ?? 0);
			default: return this.teamSortKey(a).localeCompare(this.teamSortKey(b), undefined, { numeric: true });
		}
	}

	/** Apply the active sort to one bucket. Buckets are sorted INDEPENDENTLY — see the
	 *  activeRecords/otherRecords comment for why they never merge. */
	private sortRecords(records: AccountingRecordDto[]): AccountingRecordDto[] {
		const col = this.sortColumn();
		const dir = this.sortDir() === 'asc' ? 1 : -1;
		return [...records].sort((a, b) => this.compareBy(col, a, b) * dir);
	}

	/** Live-money records, in the active sort order.
	 *
	 *  The active / waitlist-dropped split SURVIVES sorting (AR-023 decision): the two buckets
	 *  are sorted independently and never merge. Flattening them would let a dropped or
	 *  waitlisted registration's charges sit between two live transactions with only a row
	 *  colour to tell them apart — a director reading a balance would have to re-derive which
	 *  rows count. Consequence to expect: sorting by date yields two chronological runs
	 *  separated by the divider, not one. */
	activeRecords = computed(() => {
		const other = this.otherGroupKeys();
		const base = other.size === 0
			? this.records()
			: this.records().filter(r => { const k = this.recordKey(r); return !k || !other.has(k); });
		return this.sortRecords(base);
	});

	/** Waitlisted / dropped / inactive records, sorted independently of the active bucket. */
	otherRecords = computed(() => {
		const other = this.otherGroupKeys();
		if (other.size === 0) return [];
		return this.sortRecords(this.records().filter(r => { const k = this.recordKey(r); return k != null && other.has(k); }));
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

	// ── Refund mode state ──
	refundRecord = signal<AccountingRecordDto | null>(null);
	showRefundConfirm = signal(false);
	/** Entered amount. NULL = nothing typed yet. The entry form no longer pre-fills the
	 *  balance, so a freshly opened modal is never submittable (AR-042). */
	amount = signal<number | null>(null);

	/** The entered amount as a number, 0 when blank - for derivation and display only.
	 *  Submit gates read amount() directly so a blank field can never pass as a zero. */
	amountValue = computed(() => this.amount() ?? 0);
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
	 *  When the team is rostered by a club rep, the team name is prefixed with the owning club
	 *  ("AgeGroup · ClubName: TeamName") so a director can tell which club a registered team
	 *  belongs to. Lets a director tell which team a transaction belongs to when a parent
	 *  registered several players. Null when the record carries no assigned-team stamp
	 *  (single-player / club-rep paths, or a player not yet on a team). */
	ownerTeamLabel(record: AccountingRecordDto): string | null {
		const team = record.ownerTeamName?.trim();
		if (!team) return null;
		const club = record.ownerClubName?.trim();
		const teamLabel = club ? `${club}: ${team}` : team;
		const ageGroup = record.ownerAgeGroupName?.trim();
		return ageGroup ? `${ageGroup} · ${teamLabel}` : teamLabel;
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
		this.paymentType() === 'check' && this.amountValue() > this.checkBalanceDue()
	);

	/** Correction bounds — corrections are SIGNED. Positive (forgive) can't exceed the
	 *  balance due (same cap as a check — CkOwedTotal, processing fees removed). Negative
	 *  (claw back — raises the amount owed) has NO floor by ruling: it may charge beyond
	 *  the fee structure, so no lower cap applies. */
	correctionExceedsBounds = computed(() => {
		if (this.paymentType() !== 'correction') return false;
		return this.amountValue() > this.checkBalanceDue();
	});

	/** Negative correction typed where it can't land on one known target (club scope).
	 *  Mirrors the server guard; drives the inline error and disables Submit. */
	correctionNegativeBlocked = computed(() =>
		this.paymentType() === 'correction' && this.amountValue() < 0 && !this.allowNegativeCorrection()
	);

	/** AR-032 — Correction is locked when the record would land on a registration whose ARB plan
	 *  is still live and the caller is not a Superuser. A correction writes the TSIC ledger ONLY;
	 *  Authorize.Net keeps drafting the original schedule, so a director "correcting" a live plan
	 *  silently diverges the two. Reads the picked target's flag when there is one (the family
	 *  ledger records against a chosen sibling), else the scope-level input. */
	correctionLocked = computed(() =>
		!this.canCorrectLivePlan() && (this.selectedAddTarget()?.arbLive ?? this.arbLive())
	);

	/** Effective CC proc rate: the host-supplied authoritative rate when given (exact
	 *  everywhere, including settled balances), else derived from balances the modal
	 *  already holds — the full-balance fee credit over the check balance (≈ principal
	 *  remaining). 0 when proc-disabled or underivable. */
	private derivedProcRate = computed(() => {
		const supplied = this.procRate();
		if (supplied !== undefined && supplied !== null) return supplied;
		const bal = this.checkBalanceDue();
		const credit = this.totalFeeReduction();
		return bal > 0 && credit > 0 ? credit / bal : 0;
	});

	/** Proc-fee effect of the ENTERED correction amount (magnitude): positive correction
	 *  removes it (like a check — the forgiven slice won't be card-paid), negative restores
	 *  it (the reinstated balance may be card-paid). Display estimate only — the backend
	 *  figure is canonical (same formula, capped at the FeeProcessingTarget). */
	correctionProcEffect = computed(() => {
		if (this.paymentType() !== 'correction') return 0;
		return Math.round(Math.abs(this.amountValue()) * this.derivedProcRate() * 100) / 100;
	});

	/** Total effect of the entered correction on the amount owed (magnitude):
	 *  |amount| + its proc effect. The single line that answers "what will this do?". */
	correctionOwedEffect = computed(() =>
		Math.abs(this.amountValue()) + this.correctionProcEffect()
	);

	// ── Net-adjustment solver (correction form) ──
	// The admin states the NET effect they want on the balance — "assess a $5 penalty"
	// (−5), "give them $50 off" (50) — and the solver computes the correction to book:
	// a correction A moves owed by A × (1 + rate), so A = net / (1 + rate). The proc-fee
	// math is handled for the admin; "Use" stamps the result into Amount. Same sign
	// convention as the Amount field: positive forgives, negative assesses.

	/** Desired net change to the balance; null = solver idle. */
	netAdjust = signal<number | null>(null);

	/** Correction amount that produces the requested net change (null when idle or 0). */
	netCorrectionAmount = computed<number | null>(() => {
		const net = this.netAdjust();
		if (net == null || net === 0 || this.paymentType() !== 'correction') return null;
		const a = Math.round((net / (1 + this.derivedProcRate())) * 100) / 100;
		return a === 0 ? null : a;
	});

	setNetAdjust(value: number | null): void {
		this.netAdjust.set(value == null || Number.isNaN(value) ? null : Math.round(value * 100) / 100);
	}

	applyNetAdjust(): void {
		const a = this.netCorrectionAmount();
		if (a != null) this.amount.set(a);
	}

	/** The full outstanding figure for the ACTIVE tab: CC charges gross owed, check and
	 *  correction use the balance with processing fees removed. Offered as a one-click
	 *  fill (AR-042) instead of being written into the field on open. */
	fullBalanceForType = computed(() =>
		this.paymentType() === 'cc' ? this.modalOwed() : this.checkBalanceDue()
	);

	/** Same convenience the pre-fill used to give, as a deliberate act. Mirrors
	 *  applyNetAdjust(): the component computes the figure, the human chooses it. */
	applyFullBalance(): void {
		this.amount.set(this.fullBalanceForType());
	}

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
	 *  balances now bound the amount caps (checkBalanceDue / modalOwed). */
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

	/** Seed the form defaults once a target is settled (or none is needed). AR-042: the
	 *  amount is deliberately left BLANK. Pre-filling it with the balance made the modal
	 *  submittable the instant it opened - Check is the default tab and its only gates were
	 *  amt > 0 && amt <= balance, both satisfied by the pre-fill, so two clicks and zero
	 *  keystrokes posted a full-balance payment and activated the registration. The figure
	 *  is still one click away via applyFullBalance(). */
	private beginNormalEntry(): void {
		this.paymentType.set('check');
		this.amount.set(null);
	}

	/** Clear all entry fields (called before either the picker or the form is shown). */
	private clearPaymentForm(): void {
		// Amount belongs here too: it is an entry field, and leaving it out let the previous
		// entry's figure survive into the target picker (AR-042).
		this.amount.set(null);
		this.netAdjust.set(null);
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

	/** True while a mouse press that STARTED on the backdrop is still down. */
	private backdropPressStarted = false;

	/** Arm backdrop dismissal only when the press lands on the backdrop itself — presses inside the
	 *  card bubble here too, and those must never arm it. */
	onBackdropPress(event: MouseEvent): void {
		this.backdropPressStarted = event.target === event.currentTarget;
	}

	/** Dismiss only when the release ALSO lands on the backdrop. Press-inside/release-outside (the
	 *  AR-009 data-loss case) and press-outside/release-inside both fall through and do nothing. */
	onBackdropRelease(event: MouseEvent): void {
		const startedAndEndedOnBackdrop = this.backdropPressStarted && event.target === event.currentTarget;
		this.backdropPressStarted = false;
		if (startedAndEndedOnBackdrop) this.closePaymentModal();
	}

	closePaymentModal(): void {
		this.backdropPressStarted = false;
		this.showPaymentModal.set(false);
		this.refundRecord.set(null);
		this.showRefundConfirm.set(false);
		this.pickingTarget.set(false);
		this.selectedAddTarget.set(null);
	}

	/** Restrict amount to 2 decimal places. A cleared field stays NULL rather than
	 *  collapsing to 0, so "nothing entered" never reads as "zero dollars". */
	setAmount(value: number | null): void {
		this.amount.set(value == null ? null : Math.round(value * 100) / 100);
	}

	selectPaymentType(type: PaymentType): void {
		// The Correction button is disabled when locked; this is the second door on the same rule.
		if (type === 'correction' && this.correctionLocked()) return;
		this.paymentType.set(type);
		// AR-042: clear rather than re-seed. Seeding here re-armed the form on every tab
		// click, which would have left the same one-click post open to anyone who picked
		// the Check tab deliberately. The per-type figure is offered by applyFullBalance().
		this.amount.set(null);
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
		// AR-042: a blank field is not a zero. Nothing submits until a human types a figure.
		if (amt == null) return false;

		if (type === 'cc') {
			return amt > 0 && amt <= this.modalOwed()
				&& !!this.ccNumber() && !!this.ccExpiry() && !!this.ccCvv()
				&& !!this.ccFirstName() && !!this.ccLastName();
		}
		if (type === 'check') {
			// AR-042: the check number is required. Trimmed - a space is not a number. It is
			// the only field that distinguishes this tab from the others on a form that
			// otherwise needs no typing, so it doubles as the tab's mode indicator.
			// NOT required for Corrections, which legitimately have no check.
			return amt > 0 && amt <= this.checkBalanceDue() && !!this.checkNo().trim();
		}
		if (type === 'refund') {
			const maxRefund = this.refundRecord()?.paidAmount ?? 0;
			return amt > 0 && amt <= maxRefund;
		}
		if (type === 'correction') {
			// Signed: positive forgives (capped at balance due), negative claws back
			// (no floor; needs a single known target — blocked at club scope).
			if (this.correctionLocked()) return false;
			if (amt === 0) return false;
			if (amt < 0) return this.allowNegativeCorrection();
			return amt <= this.checkBalanceDue();
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
		// Non-null by canSubmitPayment(), which is the only door to this method.
		const amt = this.amountValue();

		if (type === 'refund') {
			const record = this.refundRecord();
			if (record?.aId) {
				this.refundSubmitted.emit({
					accountingRecordId: record.aId,
					refundAmount: amt,
					comment: this.comment().trim() || null
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
				amount: amt,
				comment: this.comment() || null
			});
		} else {
			this.checkSubmitted.emit({
				amount: amt,
				checkNo: this.checkNo().trim() || null,
				comment: this.comment() || null,
				paymentType: type === 'check' ? 'Check' : 'Correction'
			});
		}

		this.closePaymentModal();
	}
}
