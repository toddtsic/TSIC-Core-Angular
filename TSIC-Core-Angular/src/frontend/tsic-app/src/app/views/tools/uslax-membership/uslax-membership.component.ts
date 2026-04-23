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
 * Default email subject/body for the USLax reconciliation page. Tokens are substituted
 * server-side per recipient through the global TextSubstitutionService engine (same
 * engine as search/registrations email and the confirmation flows). !PERSON is the
 * canonical person token; !PLAYER still works as a legacy alias so older saved bodies
 * don't break.
 *
 * Copy deliberately REPORTS status rather than asserting a problem — this lets the
 * same body make sense for any recipient status. Guidance sections are headed by
 * "If your status is X" so recipients self-route. The server also skips sending to
 * members already in good standing, so even if the admin force-selects them they
 * don't get a message (see UsLaxMembershipService.NeedsAction).
 */
const DEFAULT_SUBJECT = '!JOBNAME: Your USA Lacrosse Membership Status';

const USLAX_DETAILS_BLOCK = `<p>The USA Lacrosse Membership on file for your !JOBNAME registration:</p>
<ul>
  <li>Name: !PERSON</li>
  <li>Date of Birth: !PLAYERDOB</li>
  <li>Membership ID: !USLAXMEMBERID</li>
  <li>Membership Status: !USLAXMEMBERSTATUSSTATUS</li>
  <li>Age Verification Status: !USLAXAGEVERIFIED</li>
  <li>Expiration Date: !USLAXEXPIRY</li>
</ul>`;

const USLAX_COMMON_GUIDANCE = `<p>Your membership must be <strong>Active</strong> and valid through the dates required for !JOBNAME. If the information above is not correct, or your status is anything other than Active, follow the guidance below for your situation.</p>
<hr>
<p><strong>If your status is PENDING or SUSPENDED</strong></p>
<p>This is usually related to USA Lacrosse's Age Verification requirements. See <a href="https://www.usalacrosse.com/age-verification">https://www.usalacrosse.com/age-verification</a> for details.</p>
<ul>
  <li>If Age Verification shows <em>Not Initiated</em>, follow the steps at the link above.</li>
  <li>If Age Verification shows <em>Pending Review</em>, USA Lacrosse is reviewing your documentation.</li>
  <li>If Age Verification shows <em>Failed Verification</em>, please resubmit documentation.</li>
  <li>Questions: <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or 410-235-6882.</li>
</ul>
<p><strong>If your status is INACTIVE or your membership is expired</strong></p>
<ul>
  <li>Go to <a href="https://account.usalacrosse.com/login">https://account.usalacrosse.com/login</a> to renew or update your membership.</li>
</ul>`;

const DEFAULT_PLAYER_BODY = `<p>Hello !PERSON,</p>
${USLAX_DETAILS_BLOCK}
${USLAX_COMMON_GUIDANCE}
<p><strong>If your status is ACTIVE but the Name or DOB above is wrong</strong></p>
<p>The DOB and Last Name on your TeamSportsInfo.com registration must match what USA Lacrosse has on file, and your USA Lacrosse membership must include a <em>Player</em> involvement.</p>
<ol>
  <li>Login to !JOBLINK</li>
  <li>Select your !PERSON registration for !JOBNAME</li>
  <li>Select 'Player Registration' from the 'Player' dropdown at the top right</li>
  <li>Click 'Next' to review/edit Last Name, DOB, and USA Lacrosse number</li>
  <li>Click 'Submit Registration(s)' to save changes</li>
</ol>
<p>If the data on the USA Lacrosse membership itself is incorrect, contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882.</p>
<p>Thank you!</p>`;

const DEFAULT_COACH_BODY = `<p>Hello !PERSON,</p>
${USLAX_DETAILS_BLOCK}
${USLAX_COMMON_GUIDANCE}
<p><strong>If your status is ACTIVE but the Name or DOB above is wrong</strong></p>
<p>The DOB and Last Name on your TeamSportsInfo.com registration must match what USA Lacrosse has on file. Login to !JOBLINK, open your !JOBNAME registration, and correct any discrepancies on the Registration form.</p>
<p>If the data on the USA Lacrosse membership itself is incorrect, contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call 410-235-6882.</p>
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

	/** Rows whose USLax state warrants action — mirrors server-side NeedsAction. */
	readonly rowsNeedingAction = computed(() =>
		this.rows().filter(r => this.needsAction(r))
	);

	readonly recipientsWithEmail = computed(() => this.selectedRows().filter(r => !!r.email));
	readonly selectedMissingEmail = computed(() => this.selectedRows().length - this.recipientsWithEmail().length);
	/** Selected rows that the server will skip because they're already in good standing. */
	readonly selectedHealthy = computed(() =>
		this.selectedRows().filter(r => !this.needsAction(r))
	);
	/** Selected rows that actually will get an email (selected AND has email AND needs action). */
	readonly effectiveRecipientCount = computed(() =>
		this.recipientsWithEmail().filter(r => this.needsAction(r)).length
	);

	readonly emailDisabledReason = computed(() => {
		if (this.selectedRows().length === 0) return 'Select one or more rows to email.';
		if (this.recipientsWithEmail().length === 0) return 'Selected rows have no email address on file.';
		if (this.effectiveRecipientCount() === 0) return 'All selected rows are already in good standing — nothing to send.';
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
		// Defer to next microtask — Syncfusion's `rowSelected`/`rowDeselected` fire before
		// `getSelectedRecords()` reflects the new state, especially for header select-all
		// which batches. Reading after the microtask gives the settled selection set.
		Promise.resolve().then(() => {
			const selected = (grid.getSelectedRecords() as UsLaxReconciliationRowDto[]) ?? [];
			this.selectedRows.set([...selected]);
		});
	}

	// Compose panel --------------------------------------------------------------------

	openCompose(): void {
		if (this.recipientsWithEmail().length === 0) return;
		if (!this.subject().trim() && !this.body().trim()) this.loadDefaultTemplate();
		this.showCompose.set(true);
		// After render, focus the subject field so the panel is visible and actionable.
		setTimeout(() => {
			const el = document.getElementById('uslaxSubject');
			el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
			(el as HTMLInputElement | null)?.focus({ preventScroll: true });
		}, 0);
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
		const recipients: UsLaxEmailRecipientDto[] = this.recipientsWithEmail()
			.filter(r => this.needsAction(r))
			.map(r => ({
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

		if (recipients.length === 0) {
			this.toast.show('No recipients need action — all selected rows are in good standing.', 'warning', 4000);
			return;
		}

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
				const parts: string[] = [`Sent ${response.sent} of ${recipients.length}`];
				if (response.failed > 0) parts.push(`${response.failed} failed`);
				if (response.missingEmail > 0) parts.push(`${response.missingEmail} had no email`);
				if (response.skippedHealthy > 0) {
					parts.push(`${response.skippedHealthy} skipped (already in good standing)`);
				}
				const msg = parts.join(', ') + '.';
				const level: 'success' | 'warning' =
					response.failed > 0 || response.skippedHealthy > 0 ? 'warning' : 'success';
				this.toast.show(msg, level, 6000);
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

	// Needs-action evaluator ----------------------------------------------------------

	/**
	 * Mirrors the server-side `NeedsAction` check. A row warrants an email when:
	 *   - the USLax ping errored, OR
	 *   - status is anything other than Active, OR
	 *   - no expiry on file, OR
	 *   - expiry is before the job's USLax-valid-through cutoff (when set).
	 * Keep this logic in sync with `UsLaxMembershipService.NeedsAction` on the server.
	 */
	needsAction(row: UsLaxReconciliationRowDto): boolean {
		if (row.statusCode !== 200) return true;
		const status = (row.memStatus ?? '').trim();
		if (status === '' || status.toLowerCase() !== 'active') return true;
		const expiryRaw = row.newExpiryDate ?? row.previousExpiryDate;
		if (!expiryRaw) return true;
		const cutoff = this.jobValidThrough();
		if (cutoff) {
			const expiryDate = new Date(expiryRaw);
			if (!isNaN(expiryDate.getTime()) && expiryDate < cutoff) return true;
		}
		return false;
	}

	// Quick-select ---------------------------------------------------------------------

	selectRowsNeedingAction(): void {
		const grid = this.gridRef;
		if (!grid) return;
		const view = (grid.getCurrentViewRecords() as UsLaxReconciliationRowDto[]) ?? [];
		const indices = view
			.map((row, i) => this.needsAction(row) ? i : -1)
			.filter(i => i >= 0);
		grid.clearSelection();
		if (indices.length > 0) grid.selectRows(indices);
		// Sync our mirror after selection settles.
		Promise.resolve().then(() => {
			const selected = (grid.getSelectedRecords() as UsLaxReconciliationRowDto[]) ?? [];
			this.selectedRows.set([...selected]);
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
			case 'Action':
				args.value = this.needsAction(d) ? 'Needs action' : 'OK';
				break;
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
