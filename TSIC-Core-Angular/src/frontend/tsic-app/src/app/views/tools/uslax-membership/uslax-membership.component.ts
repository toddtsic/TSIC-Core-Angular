import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { UsLaxMembershipService } from '@infrastructure/services/uslax-membership.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
	UsLaxEmailRecipientDto,
	UsLaxMembershipRole,
	UsLaxReconciliationCandidateDto,
	UsLaxReconciliationRowDto
} from '@core/api';

// C# enum generates as `type UsLaxMembershipRole = number` — mirror legible names at call sites.
const MEMBERSHIP_ROLE = { Player: 0, Coach: 1 } as const satisfies Record<'Player' | 'Coach', UsLaxMembershipRole>;

/**
 * Default email subject/body ported verbatim from legacy USLaxMembershipController.Index (BRunAll=true).
 * Tokens (!PLAYER, !PLAYERDOB, !USLAXMEMBERID, !USLAXMEMBERSTATUSSTATUS, !USLAXAGEVERIFIED,
 * !USLAXEXPIRY, !JOBNAME, !JOBLINK) are substituted server-side per recipient.
 */
const DEFAULT_SUBJECT = '!JOBNAME: USA Lacrosse Membership Status';

const DEFAULT_PLAYER_BODY = `<p>According to our records, the USA Lacrosse Membership for !PLAYER does not currently meet the validation requirements for !JOBNAME.</p>
<p>USA Lacrosse Membership Details for !PLAYER (!PLAYERDOB):</p>
<ul>
  <li>Membership ID: !USLAXMEMBERID</li>
  <li>Membership Status: !USLAXMEMBERSTATUSSTATUS</li>
  <li>Age Verification Status: !USLAXAGEVERIFIED</li>
  <li>Membership Expiration Date: !USLAXEXPIRY</li>
</ul>
<p>If your membership is <strong>PENDING</strong> or <strong>SUSPENDED</strong>:</p>
<p>This is most likely due to USA Lacrosse's Age Verification requirements as of 7/1/25. For more information, go to <a href="https://www.usalacrosse.com/age-verification">https://www.usalacrosse.com/age-verification</a>.</p>
<ul>
  <li>If your Age Verification status is 'Not Initiated' please follow the steps in the link above.</li>
  <li>Once documentation has been submitted, your Age Verification status will be 'Pending Review' until it is verified by USA Lacrosse.</li>
  <li>If your Age Verification status indicates your uploaded document Failed Verification, please resubmit documentation.</li>
  <li>Once documentation is submitted, please contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882 with any questions about timelines for verification.</li>
</ul>
<p>If your membership is <strong>ACTIVE</strong>:</p>
<p>The DOB and Last Name spelling on your TeamSportsInfo.com registration must match what USA Lacrosse has on file, and the membership type must be for a Player. To review your registration and correct any discrepancies:</p>
<ol>
  <li>Login to !JOBLINK</li>
  <li>Select the role !JOBNAME for !PLAYER</li>
  <li>Select 'Player Registration' from the 'Player' dropdown menu at the top right</li>
  <li>Click 'Next' to review/edit Player Last Name, DOB, and/or USA Lacrosse number on the Registration page</li>
  <li>Click 'Submit Registration(s)' at the bottom of the page to submit any changes</li>
</ol>
<p>If the data on the USA Lacrosse membership is incorrect, please contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882.</p>
<p>Note: the membership must be valid through the dates required for !JOBNAME. If your membership does not meet these requirements, go to <a href="https://account.usalacrosse.com/login">https://account.usalacrosse.com/login</a> to renew and/or update your membership.</p>
<p>If your membership is <strong>INACTIVE</strong>:</p>
<ul>
  <li>Go to <a href="https://account.usalacrosse.com/login">https://account.usalacrosse.com/login</a> to renew and/or update your membership.</li>
</ul>
<p>For assistance, please contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882.</p>
<p>Thank you!</p>`;

const DEFAULT_COACH_BODY = `<p>According to our records, the USA Lacrosse Membership for !PLAYER does not currently meet the coach/staff validation requirements for !JOBNAME.</p>
<p>USA Lacrosse Membership Details for !PLAYER (!PLAYERDOB):</p>
<ul>
  <li>Membership ID: !USLAXMEMBERID</li>
  <li>Membership Status: !USLAXMEMBERSTATUSSTATUS</li>
  <li>Age Verification Status: !USLAXAGEVERIFIED</li>
  <li>Membership Expiration Date: !USLAXEXPIRY</li>
</ul>
<p>If your membership is <strong>PENDING</strong> or <strong>SUSPENDED</strong>:</p>
<p>This is most likely due to USA Lacrosse's Age Verification / background-check requirements. For more information, go to <a href="https://www.usalacrosse.com/age-verification">https://www.usalacrosse.com/age-verification</a>.</p>
<p>If your membership is <strong>ACTIVE</strong>:</p>
<p>Please confirm the DOB and Last Name on your TeamSportsInfo.com registration match what USA Lacrosse has on file. Login to !JOBLINK, open your registration, and correct any discrepancies.</p>
<p>If your membership is <strong>INACTIVE</strong>:</p>
<ul>
  <li>Go to <a href="https://account.usalacrosse.com/login">https://account.usalacrosse.com/login</a> to renew and/or update your membership.</li>
</ul>
<p>For assistance, please contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882.</p>
<p>Thank you!</p>`;

@Component({
	selector: 'app-uslax-membership',
	standalone: true,
	imports: [DatePipe, NgClass, FormsModule, GridAllModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-membership.component.html',
	styleUrl: './uslax-membership.component.scss'
})
export class UsLaxMembershipComponent implements OnInit {
	private readonly service = inject(UsLaxMembershipService);
	private readonly jobService = inject(JobService);
	private readonly toast = inject(ToastService);

	readonly MEMBERSHIP_ROLE = MEMBERSHIP_ROLE;

	readonly role = signal<UsLaxMembershipRole>(MEMBERSHIP_ROLE.Player);
	readonly candidates = signal<UsLaxReconciliationCandidateDto[]>([]);
	readonly rows = signal<UsLaxReconciliationRowDto[]>([]);
	readonly isLoadingCandidates = signal(false);
	readonly isReconciling = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly summary = signal<{ totalPinged: number; datesUpdated: number; failed: number } | null>(null);

	readonly selectedRows = signal<UsLaxReconciliationRowDto[]>([]);

	// Compose panel state
	readonly showCompose = signal(false);
	readonly subject = signal('');
	readonly body = signal('');
	readonly isSending = signal(false);

	private gridRef?: GridComponent;

	readonly jobValidThrough = computed(() => {
		const raw = this.jobService.currentJob()?.usLaxNumberValidThroughDate;
		if (!raw) return null;
		const d = new Date(raw);
		return isNaN(d.getTime()) ? null : d;
	});

	readonly canReconcile = computed(() => this.candidates().length > 0 && !this.isReconciling());

	readonly isCoachRole = computed(() => this.role() === MEMBERSHIP_ROLE.Coach);

	readonly eligibleLabel = computed(() => this.isCoachRole() ? 'Eligible coaches' : 'Eligible players');
	readonly eligibleNote = computed(() =>
		this.isCoachRole()
			? 'Active unassigned adults with a USA Lacrosse number on file'
			: 'Active Lacrosse Players with a membership ID on file'
	);

	readonly gridToolbar: ToolbarItems[] = ['ExcelExport'];
	readonly selectionSettings = { type: 'Multiple' as const, checkboxOnly: true };

	readonly recipientsWithEmail = computed(() => this.selectedRows().filter(r => !!r.email));
	readonly selectedMissingEmail = computed(() => this.selectedRows().length - this.recipientsWithEmail().length);

	readonly emailDisabledReason = computed(() => {
		if (this.selectedRows().length === 0) return 'Select one or more rows to email.';
		if (this.recipientsWithEmail().length === 0) return 'Selected rows have no email address on file.';
		return null;
	});

	readonly canSendEmail = computed(() =>
		!this.isSending() &&
		this.recipientsWithEmail().length > 0 &&
		this.subject().trim().length > 0 &&
		this.body().trim().length > 0
	);

	// Lifecycle ------------------------------------------------------------------------

	ngOnInit(): void {
		this.loadCandidates();
	}

	setRole(next: UsLaxMembershipRole): void {
		if (this.role() === next) return;
		this.role.set(next);
		this.rows.set([]);
		this.summary.set(null);
		this.selectedRows.set([]);
		this.closeCompose();
		this.loadCandidates();
	}

	loadCandidates(): void {
		this.isLoadingCandidates.set(true);
		this.errorMessage.set(null);
		this.selectedRows.set([]);
		this.service.getCandidates(this.role()).subscribe({
			next: list => {
				this.candidates.set(list);
				this.isLoadingCandidates.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load candidates.');
				this.isLoadingCandidates.set(false);
			}
		});
	}

	reconcileAll(): void {
		if (!this.canReconcile()) return;
		this.isReconciling.set(true);
		this.rows.set([]);
		this.summary.set(null);
		this.selectedRows.set([]);
		this.service.reconcile({ role: this.role() }).subscribe({
			next: response => {
				this.rows.set(response.rows);
				this.summary.set({
					totalPinged: response.totalPinged,
					datesUpdated: response.datesUpdated,
					failed: response.failed
				});
				this.isReconciling.set(false);
				const memberWord = response.totalPinged === 1 ? 'membership' : 'memberships';
				const dateWord = response.datesUpdated === 1 ? 'expiry date' : 'expiry dates';
				const failedNote = response.failed > 0
					? `, ${response.failed} ${response.failed === 1 ? 'check' : 'checks'} failed`
					: '';
				const msg = `Checked ${response.totalPinged} ${memberWord}. ${response.datesUpdated} ${dateWord} updated${failedNote}.`;
				this.toast.show(msg, response.failed > 0 ? 'warning' : 'success', 5000);
				this.loadCandidates();
			},
			error: err => {
				this.isReconciling.set(false);
				this.toast.show(`Reconciliation failed: ${err?.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	// Grid selection -------------------------------------------------------------------

	onSelectionChanged(grid: GridComponent): void {
		this.gridRef = grid;
		const selected = (grid.getSelectedRecords() as UsLaxReconciliationRowDto[]) ?? [];
		this.selectedRows.set([...selected]);
	}

	// Compose panel --------------------------------------------------------------------

	openCompose(): void {
		if (this.recipientsWithEmail().length === 0) return;
		if (!this.subject().trim() && !this.body().trim()) this.loadDefaultTemplate();
		this.showCompose.set(true);
	}

	closeCompose(): void {
		this.showCompose.set(false);
	}

	loadDefaultTemplate(): void {
		this.subject.set(DEFAULT_SUBJECT);
		this.body.set(this.isCoachRole() ? DEFAULT_COACH_BODY : DEFAULT_PLAYER_BODY);
	}

	clearCompose(): void {
		this.subject.set('');
		this.body.set('');
	}

	send(): void {
		if (!this.canSendEmail()) return;
		const recipients: UsLaxEmailRecipientDto[] = this.recipientsWithEmail().map(r => ({
			registrationId: r.registrationId,
			firstName: r.firstName,
			lastName: r.lastName,
			email: r.email,
			dob: null,
			membershipId: r.membershipId,
			memStatus: r.memStatus ?? null,
			ageVerified: r.ageVerified ?? null,
			expiryDate: r.newExpiryDate ?? r.previousExpiryDate ?? null
		}));

		const confirmMsg = `Send this email to ${recipients.length} recipient${recipients.length === 1 ? '' : 's'}?`;
		if (!confirm(confirmMsg)) return;

		this.isSending.set(true);
		this.service.sendEmail({
			subject: this.subject(),
			body: this.body(),
			recipients
		}).subscribe({
			next: response => {
				this.isSending.set(false);
				const missingNote = response.missingEmail > 0 ? `, ${response.missingEmail} had no email` : '';
				const failedNote = response.failed > 0 ? `, ${response.failed} failed` : '';
				const msg = `Sent ${response.sent} of ${recipients.length}${failedNote}${missingNote}.`;
				this.toast.show(msg, response.failed > 0 ? 'warning' : 'success', 5000);
				this.showCompose.set(false);
				this.gridRef?.clearSelection();
				this.selectedRows.set([]);
			},
			error: err => {
				this.isSending.set(false);
				this.toast.show(`Email send failed: ${err?.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	// Grid formatting helpers ---------------------------------------------------------

	/** Stamp row numbers after data binds or after a sort/page action reshuffles the view. */
	refreshRowNumbers(grid: GridComponent): void {
		this.gridRef = grid;
		const gridEl = grid.element;
		if (!gridEl) return;
		grid.getRows().forEach((row, i) => {
			const cell = row.querySelector('td.row-number-cell');
			if (cell) cell.textContent = String(i + 1);
		});
	}

	onActionComplete(args: { requestType?: string }, grid: GridComponent): void {
		if (args.requestType === 'sorting' || args.requestType === 'paging' || args.requestType === 'refresh') {
			this.refreshRowNumbers(grid);
		}
	}

	padMembershipId(id: string | number | null | undefined): string {
		if (id == null) return '';
		const digits = String(id).replace(/\D/g, '');
		if (!digits) return '';
		return digits.padStart(12, '0');
	}

	rowClass(row: UsLaxReconciliationRowDto): string {
		if (row.statusCode !== 200) return 'row-error';
		if (row.expiryDateUpdated) return 'row-updated';
		if (row.memStatus === 'Inactive') return 'row-inactive';
		return '';
	}

	involvementBadges(row: UsLaxReconciliationRowDto): string[] {
		const inv = row.involvement;
		if (!Array.isArray(inv)) return [];
		return inv.filter((x): x is string => typeof x === 'string');
	}

	ageVerifiedDisplay(row: UsLaxReconciliationRowDto): string {
		const v = row.ageVerified;
		if (v == null) return '';
		return v.toString().toLowerCase() === 'true' ? 'Yes' : 'No';
	}

	onGridToolbarClick(args: { item?: { id?: string } }, grid: GridComponent): void {
		if (args.item?.id?.endsWith('_excelexport')) {
			const roleWord = this.isCoachRole() ? 'Coaches' : 'Players';
			grid.excelExport({ fileName: `USLaxMembershipReconciliation_${roleWord}.xlsx` });
		}
	}

	onExcelQueryCellInfo(args: { column: { headerText: string }; data: UsLaxReconciliationRowDto; value: unknown }): void {
		const d = args.data;
		switch (args.column.headerText) {
			case '#': {
				const view = (this.gridRef?.getCurrentViewRecords() as UsLaxReconciliationRowDto[]) ?? this.rows();
				args.value = view.findIndex(r => r.registrationId === d.registrationId) + 1;
				break;
			}
			case 'Name':
				args.value = `${d.lastName}, ${d.firstName}`;
				break;
			case 'Member ID':
				args.value = this.padMembershipId(d.membershipId);
				break;
			case 'Status':
				args.value = d.statusCode !== 200 ? 'API error' : (d.memStatus ?? 'Unknown');
				break;
			case 'Age Verified':
				args.value = this.ageVerifiedDisplay(d);
				break;
			case 'Involvement':
				args.value = this.involvementBadges(d).join(', ');
				break;
			case 'Previous Expiry':
				args.value = d.previousExpiryDate ? new Date(d.previousExpiryDate).toLocaleDateString() : '';
				break;
			case 'New Expiry':
				args.value = d.newExpiryDate ? new Date(d.newExpiryDate).toLocaleDateString() : '';
				break;
			case 'Details':
				args.value = d.errorMessage ?? '';
				break;
		}
	}
}
