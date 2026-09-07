import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { GridRowNumbersDirective } from '@shared-ui/directives/grid-row-numbers.directive';
import { EmailBodyEditorComponent } from '@shared-ui/components/email-body-editor/email-body-editor.component';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { environment } from '@environments/environment';
import { UsLaxMembershipService } from '@infrastructure/services/uslax-membership.service';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
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
 * Keys from `UsLaxEligibilityPolicy.Describe` that back the three Yes/No columns. A key is absent
 * from a row whenever the checklist stopped before reaching it (vendor unreachable, no record
 * found, validation bypassed) — that reads as NOT ASSESSED, never as a No.
 *
 * `validThrough` carries two keys because the policy emits `NoCutoffConfigured` in place of
 * `ExpiresBeforeCutoff` when the event has no USA Lacrosse cutoff date set.
 */
const CHECK_KEYS: Record<'dob' | 'lastName' | 'validThrough', readonly string[]> = {
	dob: ['DobMismatch'],
	lastName: ['LastNameMismatch'],
	validThrough: ['ExpiresBeforeCutoff', 'NoCutoffConfigured']
};

/** Every criterion that has a column of its own. Anything else the checklist fails on is "Other". */
const NAMED_CHECK_KEYS: ReadonlySet<string> = new Set(Object.values(CHECK_KEYS).flat());

/**
 * The grid's row: the reconciliation DTO plus five PRE-RESOLVED display strings.
 *
 * AR-071 parts 1 and 3 are the same problem. ej2 filters and sorts a column by its `field`, and
 * these verdicts are derived — four of them out of the `checks` ARRAY, which no column can
 * filter. Projecting them onto real string fields is what makes those headers filterable at all,
 * and it drops two hand-written sort comparers that existed only because the columns had no field
 * worth ordering by.
 */
type UsLaxGridRow = UsLaxReconciliationRowDto & {
	needsEmail: string;
	lastNameMatch: string;
	dobMatch: string;
	meetsValidThrough: string;
	otherIssue: string;
};

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
	imports: [DatePipe, NgClass, FormsModule, GridAllModule, GridRowNumbersDirective, EmailBodyEditorComponent, TestSendButtonComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-membership.component.html',
	styleUrl: './uslax-membership.component.scss'
})
export class UsLaxMembershipComponent implements OnInit {
	private readonly service = inject(UsLaxMembershipService);
	private readonly jobService = inject(JobService);
	private readonly toast = inject(ToastService);
	private readonly auth = inject(AuthService);

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

	readonly filterSettings = { type: 'Excel' as const };

	/** What a Yes/No column shows for a criterion that was never assessed. NEVER "No" — see AR-044. */
	private static readonly NOT_ASSESSED = '—';

	/**
	 * What the grid actually binds: every row with its derived verdicts resolved to plain strings
	 * so ej2 can filter and sort them. Pure projection — the signal `rows()` stays the source.
	 */
	readonly gridRows = computed<UsLaxGridRow[]>(() =>
		this.rows().map(r => ({
			...r,
			needsEmail: this.needsAction(r) ? 'Yes' : 'No',
			lastNameMatch: this.verdictOf(r, CHECK_KEYS.lastName),
			dobMatch: this.verdictOf(r, CHECK_KEYS.dob),
			meetsValidThrough: this.verdictOf(r, CHECK_KEYS.validThrough),
			otherIssue: this.otherIssueOf(r)
		}))
	);

	readonly gridToolbar: ToolbarItems[] = ['ExcelExport'];
	readonly selectionSettings = { type: 'Multiple' as const, checkboxOnly: true };

	/** Rows whose USLax state warrants action — mirrors server-side NeedsAction. */
	readonly rowsNeedingAction = computed(() =>
		this.rows().filter(r => this.needsAction(r))
	);


	/** Selected rows that the server will skip because they're already in good standing. */
	readonly selectedHealthy = computed(() =>
		this.selectedRows().filter(r => !this.needsAction(r))
	);
	/**
	 * Selected rows that will get an email: selected AND needing action.
	 *
	 * Deliberately NOT filtered on the row's own address. That column is the PLAYER's account
	 * email, and a player who has none almost always still has a mom and a dad on file. Filtering
	 * here dropped those rows before they were ever posted, so the server never got the chance to
	 * resolve the family — which made the whole screen player-addressed no matter what the send
	 * path did. Addressing is the server's job (player → mom + dad + own), and it reports what it
	 * could not reach as `missingEmail` on the start response, which the result toast prints.
	 */
	readonly effectiveRecipientCount = computed(() =>
		this.selectedRows().filter(r => this.needsAction(r)).length
	);

	readonly emailDisabledReason = computed(() => {
		if (this.selectedRows().length === 0) return 'Select one or more rows to email.';
		if (this.effectiveRecipientCount() === 0) return 'All selected rows are already in good standing — nothing to send.';
		return null;
	});

	readonly canSendEmail = computed(() =>
		!this.isSending() &&
		this.effectiveRecipientCount() > 0 &&
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
		if (this.effectiveRecipientCount() === 0) return;
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

	/** Row → recipient snapshot, shared by the real send and the test send. */
	private buildRecipients(): UsLaxEmailRecipientDto[] {
		return this.selectedRows()
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
	}

	readonly isNonProd = environment.envName !== 'production';
	/** Test popovers are SUPERUSER-only on shared environments (AM-060 rule). */
	readonly isSuperuser = this.auth.isSuperuser;
	readonly isSendingTest = signal(false);

	/** Non-prod: renders tokens against the first actionable recipient and delivers the real
	 *  email to a single test inbox. */
	sendTestEmail(options: TestSendOptions): void {
		if (!this.subject().trim() || !this.body().trim()) return;
		const recipient = this.buildRecipients()[0];
		if (!recipient) {
			this.toast.show('No recipient needing action to render the test against.', 'warning', 4000);
			return;
		}

		this.isSendingTest.set(true);
		this.service.sendTestEmail({
			subject: this.subject(),
			body: this.body(),
			recipient,
			testRecipient: options.recipient
		}).subscribe({
			next: result => {
				this.isSendingTest.set(false);
				if (result.sent) {
					this.toast.show(`Test email (rendered for ${result.renderedFor}) sent to ${result.recipient}`, 'success', 6000);
				} else {
					this.toast.show(result.message || 'Test send failed', 'danger', 5000);
				}
			},
			error: err => {
				this.isSendingTest.set(false);
				this.toast.show(`Test send failed: ${err?.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	send(): void {
		if (!this.canSendEmail()) return;
		const recipients: UsLaxEmailRecipientDto[] = this.buildRecipients();

		if (recipients.length === 0) {
			this.toast.show('No recipients need action — all selected rows are in good standing.', 'warning', 4000);
			return;
		}

		const confirmMsg = `Send this email to ${recipients.length} recipient${recipients.length === 1 ? '' : 's'}?`;
		if (!confirm(confirmMsg)) return;

		this.isSending.set(true);
		this.service.sendEmailAndAwait({
			subject: this.subject(),
			body: this.body(),
			recipients
		}).subscribe({
			next: ({ start, status }) => {
				this.isSending.set(false);
				const parts: string[] = [`Sent ${status.sent} of ${start.totalRecipients}`];
				if (status.failed > 0) parts.push(`${status.failed} failed`);
				if (status.optedOut > 0) parts.push(`${status.optedOut} unsubscribed`);
				if (start.missingEmail > 0) parts.push(`${start.missingEmail} had no email`);
				if (start.skippedHealthy > 0) {
					parts.push(`${start.skippedHealthy} skipped (already in good standing)`);
				}
				// Not the family's fault — say so plainly, and say what to do about it.
				if (start.unverifiable > 0) {
					parts.push(start.noCutoffConfigured
						? `${start.unverifiable} not checked — this event has no USA Lacrosse cutoff date set`
						: `${start.unverifiable} not checked — USA Lacrosse was unreachable, try again later`);
				}
				const msg = parts.join(', ') + '.';
				const level: 'success' | 'warning' =
					status.failed > 0 || start.skippedHealthy > 0 || start.unverifiable > 0 ? 'warning' : 'success';
				this.toast.show(msg, level, start.unverifiable > 0 ? 9000 : 6000);
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
	 * Reasons the check failed on OUR side, not the family's — USA Lacrosse unreachable, or no
	 * cutoff date configured on the job. The server refuses to email these (it would be a false
	 * alarm, and during an outage a false alarm to the entire list), so the quick-select must not
	 * offer them either.
	 */
	private static readonly UNVERIFIABLE_REASONS = ['VendorUnavailable', 'NoCutoffConfigured'];

	/**
	 * A row warrants an email when the reconcile verdict says it is not eligible, EXCEPT where the
	 * verdict failed for one of our own reasons.
	 *
	 * This reads the verdict the server already computed with `UsLaxEligibilityPolicy` — the same
	 * rule the registration form applies — rather than re-deriving one here. It used to carry its
	 * own copy of a status-and-expiry check, which is how a player whose membership was active but
	 * registered under a different name or birthdate looked fine on this screen while the front
	 * door was turning them away.
	 */
	needsAction(row: UsLaxReconciliationRowDto): boolean {
		if (UsLaxMembershipComponent.UNVERIFIABLE_REASONS.includes(row.eligibilityReason)) return false;
		return !row.eligible;
	}

	// Selectability ---------------------------------------------------------------------
	/**
	 * AR-079 (Ann, 09-06): a row that CANNOT be mailed does not offer a checkbox.
	 *
	 * Ann reported the checkboxes as inert — "the boxes are there just need them to function".
	 * They were never inert: they fed the send, which then dropped the rows already in good
	 * standing, because the server runs UsLaxEligibilityPolicy over its own data and refuses to
	 * tell a valid member their membership is broken. Ticking a healthy row and watching nothing
	 * happen is indistinguishable from a dead control, so the control is withdrawn where it has
	 * no effect. The rule is unchanged; the UI now states it instead of enforcing it silently.
	 *
	 * Belt AND braces, deliberately: the checkbox is hidden (cosmetic) and `rowSelecting` is
	 * cancelled (behavioral). Hiding alone would still let a click on the row body select it, and
	 * the ej2 class names hiding depends on could drift on a Syncfusion upgrade — the cancel is
	 * what actually holds. The server keeps its own independent filter as the real backstop.
	 */
	onRowSelecting(args: { data?: unknown; cancel?: boolean }): void {
		// Header select-all hands us an ARRAY. It is hidden in CSS, but if it ever fires, let it
		// through: every row it can reach is already a mailable one.
		if (Array.isArray(args.data)) return;
		const row = args.data as UsLaxReconciliationRowDto | undefined;
		if (row && !this.needsAction(row)) args.cancel = true;
	}

	/** Marks non-mailable rows so the stylesheet can withdraw their checkbox. */
	onRowDataBound(args: { data?: unknown; row?: Element }): void {
		const row = args.data as UsLaxReconciliationRowDto | undefined;
		if (row && !this.needsAction(row)) args.row?.classList.add('uslax-row-not-mailable');
	}

	// Quick-select ---------------------------------------------------------------------
	/**
	 * Select every row marked Needs Email and go straight to compose — the one-click version of
	 * select-then-compose, and the common case. Shares `needsAction` with the column itself, so the
	 * button and the marks can never disagree. Compose opens only after the selection mirror has
	 * settled, or it would read the previous selection's recipient count.
	 */
	emailThoseNeedingEmail(): void {
		this.selectRowsWhere(row => this.needsAction(row), () => {
			if (this.effectiveRecipientCount() > 0) this.openCompose();
		});
	}

	/** Shared selection mechanic — the quick-selects differ only in their predicate, and the
	 *  index mapping / mirror-sync below is fiddly enough that a second copy would drift. */
	private selectRowsWhere(
		predicate: (row: UsLaxReconciliationRowDto) => boolean,
		afterSelection?: () => void
	): void {
		const grid = this.gridRef;
		if (!grid) return;
		const view = (grid.getCurrentViewRecords() as UsLaxReconciliationRowDto[]) ?? [];
		const indices = view
			.map((row, i) => predicate(row) ? i : -1)
			.filter(i => i >= 0);
		grid.clearSelection();
		if (indices.length > 0) grid.selectRows(indices);
		// Sync our mirror after selection settles.
		Promise.resolve().then(() => {
			const selected = (grid.getSelectedRecords() as UsLaxReconciliationRowDto[]) ?? [];
			this.selectedRows.set([...selected]);
			afterSelection?.();
		});
	}

	// Grid formatting helpers ---------------------------------------------------------

	/** Row numbers are the `tsicRowNumbers` directive's job — this only caches the grid instance
	 * for quick-select, compose and export, which can all run before any selection event fires. */
	onGridDataBound(grid: GridComponent): void {
		this.gridRef = grid;
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

	// Eligibility checklist (AR-071) -----------------------------------------------------
	//
	// `checks` is UsLaxEligibilityPolicy.Describe — EVERY criterion judged independently. The
	// row's `eligibilityReason` / `eligibilityDetail` come from Evaluate, an ordered chain that
	// returns on the FIRST failure. That is right for a gate and wrong for a report: a player
	// whose last name AND birthdate both disagreed was shown one problem, so the director fixed
	// it, resubmitted, and failed again on the one that was never displayed.

	private checkFor(row: UsLaxReconciliationRowDto, keys: readonly string[]) {
		return row.checks?.find(c => keys.includes(c.key));
	}

	/**
	 * Yes / No / — for one criterion. A MISSING check and a null `passed` both read as NOT
	 * ASSESSED: the checklist stops early when USA Lacrosse is unreachable, returns no record, or
	 * validation is bypassed for the team, and none of those are a No. AR-044 shipped exactly
	 * that mistake on this table — a confident No to a question nobody had asked.
	 */
	private verdictOf(row: UsLaxReconciliationRowDto, keys: readonly string[]): string {
		const c = this.checkFor(row, keys);
		if (!c || c.passed === null || c.passed === undefined) return UsLaxMembershipComponent.NOT_ASSESSED;
		return c.passed ? 'Yes' : 'No';
	}

	/** Colour for a Yes/No cell. A dash is MUTED, not red — it is an unanswered question. */
	checkClass(verdict: string): string {
		if (verdict === 'No') return 'text-danger fw-semibold';
		if (verdict === 'Yes') return 'text-success-emphasis';
		return 'text-body-secondary';
	}

	/** Hover text for a Yes/No cell — the policy's own words, so cell and tooltip cannot drift. */
	checkTitle(row: UsLaxGridRow, which: 'dob' | 'lastName' | 'validThrough'): string {
		const c = this.checkFor(row, CHECK_KEYS[which]);
		if (!c) return 'Not checked — there was no USA Lacrosse record to check against.';
		// Ann asked that the birthdate itself not appear on this screen, and the policy's detail
		// line prints both dates. The DOB cell therefore never borrows it.
		if (which === 'dob') {
			if (c.passed === true) return 'Matches the birthdate USA Lacrosse has on file.';
			if (c.passed === false) return 'Does not match the birthdate USA Lacrosse has on file.';
			return 'Not checked.';
		}
		return c.detail ?? c.label;
	}

	// "Other issue?" (AR-085) — the fourth verdict, replacing the prose Details column ------
	//
	// Three of the policy's six criteria have no column of their own: no record returned, status
	// not Active, and not registered as a Player/Coach — plus a failed USA Lacrosse call. Deleting
	// Details outright would have shown those players three dashes and no explanation, which is
	// the AR-071 defect again. This column answers "did anything ELSE fail?" in the same Yes/No/—
	// language as its three neighbours, with the policy's own sentence as the hover.

	/** The checklist entries that no named column already shows. */
	private otherChecks(row: UsLaxReconciliationRowDto) {
		return (row.checks ?? []).filter(c => !NAMED_CHECK_KEYS.has(c.key));
	}

	/**
	 * Yes when any un-columned criterion failed; No when they were all assessed and passed; — when
	 * none was assessed (validation bypassed for the team or a test number — the policy returns
	 * a single null-passed row for those). A row with no checklist at all answers from its
	 * transport outcome: a failed call is a Yes, because it IS the reason nothing else was checked.
	 */
	private otherIssueOf(row: UsLaxReconciliationRowDto): string {
		const others = this.otherChecks(row);
		if (others.length === 0) {
			if (!row.checks?.length && row.statusCode !== 200) return 'Yes';
			return UsLaxMembershipComponent.NOT_ASSESSED;
		}
		if (others.some(c => c.passed === false)) return 'Yes';
		if (others.some(c => c.passed === true)) return 'No';
		return UsLaxMembershipComponent.NOT_ASSESSED;
	}

	/** Colour for the Other issue? cell — inverted from checkClass, because here Yes is the bad answer. */
	otherIssueClass(verdict: string): string {
		if (verdict === 'Yes') return 'text-danger fw-semibold';
		if (verdict === 'No') return 'text-success-emphasis';
		return 'text-body-secondary';
	}

	/** Hover text for the Other issue? cell — every un-columned criterion that did not pass, in the policy's words. */
	otherIssueTitle(row: UsLaxReconciliationRowDto): string {
		const lines = this.otherIssueLines(row);
		if (lines.length > 0) return lines.join(' ');
		if (this.otherChecks(row).some(c => c.passed === true)) return 'No other criteria failed.';
		return 'Not checked.';
	}

	/** The un-columned criteria this row did NOT pass, one sentence each. Bypass rows contribute their reason. */
	otherIssueLines(row: UsLaxReconciliationRowDto): string[] {
		const others = this.otherChecks(row);
		if (others.length === 0) {
			if (!row.checks?.length && row.errorMessage) return [row.errorMessage];
			return [];
		}
		return others.filter(c => c.passed !== true).map(c => c.detail ?? c.label);
	}

	/**
	 * Every criterion this row did NOT pass, in words, one line each. No longer a grid column
	 * (AR-085 — Ann: "we don't need Details if we have the other info"); kept for the Excel export,
	 * where a prose column costs nothing and "export it" was once this screen's only workaround.
	 *
	 * Not-assessable criteria are included: "no valid-through date is set for this event" is the
	 * most actionable line on the page and it is not a failure. Falls back to the single Evaluate
	 * sentence only if a row somehow arrives with no checklist at all.
	 */
	detailLines(row: UsLaxReconciliationRowDto): string[] {
		const checks = row.checks ?? [];
		if (checks.length === 0) return row.eligibilityDetail ? [row.eligibilityDetail] : [];
		return checks
			.filter(c => c.passed !== true)
			.map(c => CHECK_KEYS.dob.includes(c.key)
				? 'Date of birth does not match USA Lacrosse.'
				: (c.detail ?? c.label));
	}

	// Sort comparer for the Involvement TEMPLATE column ---------------------------------
	//
	// Involvement renders from a derived value rather than a single field, so ej2 has nothing to
	// order it by. A column still needs a real `field` to be sortable at all, so it is pointed at
	// a genuine row property and given a comparer that orders by what the cell actually DISPLAYS.
	// ej2 hands the comparer the two row objects as its 3rd/4th arguments, which is where the
	// derived value comes from. An arrow property, not a method — it is passed by reference into
	// the grid and would otherwise lose `this`.

	/** Orders the Involvement column by its badge text, e.g. "Player" before "Player, Official". */
	readonly involvementSortComparer = (
		_x: unknown,
		_y: unknown,
		xRow?: UsLaxReconciliationRowDto,
		yRow?: UsLaxReconciliationRowDto
	): number => {
		const a = xRow ? this.involvementBadges(xRow).join(', ') : '';
		const b = yRow ? this.involvementBadges(yRow).join(', ') : '';
		return a.localeCompare(b);
	};

	involvementBadges(row: UsLaxReconciliationRowDto): string[] {
		const inv = row.involvement;
		if (!Array.isArray(inv)) return [];
		return inv.filter((x): x is string => typeof x === 'string');
	}

	/// Shows USA Lacrosse's own answer verbatim. It is a vocabulary, not a boolean — the one
	/// value confirmed from a live capture is "Approved" (UsLaxServiceBatchTests), and Pending,
	/// Denied and Not Required are all plausible and undocumented. This previously rendered Yes
	/// only for the literal "true", which USA Lacrosse never sends, so every verified player read
	/// as a confident "No" on a compliance column. An unfamiliar word invites a question; a wrong
	/// No does not. Keep this a pass-through: the email token !USLAXAGEVERIFIED and the grid's
	/// sort both use the raw value, and all three agree only while this does too.
	ageVerifiedDisplay(row: UsLaxReconciliationRowDto): string {
		const v = row.ageVerified;
		if (v == null) return '';
		return v.toString().trim();
	}

	onGridToolbarClick(args: { item?: { id?: string } }, grid: GridComponent): void {
		if (args.item?.id?.endsWith('_excelexport')) {
			const roleWord = this.isCoachRole() ? 'Coaches' : 'Players';
			// Details is a hidden column on screen (AR-085) and a real one in the spreadsheet.
			grid.excelExport({ fileName: `USLaxMembershipReconciliation_${roleWord}.xlsx`, includeHiddenColumn: true });
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
			case 'Email?':
				args.value = this.needsAction(d) ? 'Yes' : '';
				break;
			case 'Name':
				args.value = `${d.lastName}, ${d.firstName}`;
				break;
			case 'Member ID':
				args.value = this.padMembershipId(d.membershipId);
				break;
			case 'Status':
				// Vendor status verbatim — a failed call is reported under Details, not here.
				args.value = d.memStatus ?? '';
				break;
			case 'Details': {
				// Every unmet criterion, same as the cell — an export that carried only the first
				// failure is what made "export it" the workaround for this screen in the first place.
				const lines = this.detailLines(d);
				args.value = lines.length > 0
					? lines.join(' ')
					: (d.errorMessage ?? (d.eligible ? 'Passes validation' : ''));
				break;
			}
			case 'Verified':
				args.value = this.ageVerifiedDisplay(d);
				break;
			case 'Involvement':
				args.value = this.involvementBadges(d).join(', ');
				break;
			case 'Other issue?': {
				// The verdict plus its reasons — the spreadsheet has no hover.
				const lines = this.otherIssueLines(d);
				args.value = lines.length > 0 ? `Yes — ${lines.join(' ')}` : this.otherIssueOf(d);
				break;
			}
			case 'Expiry Old':
				args.value = d.previousExpiryDate ? new Date(d.previousExpiryDate).toLocaleDateString() : '';
				break;
			case 'Expiry New':
				args.value = d.newExpiryDate ? new Date(d.newExpiryDate).toLocaleDateString() : '';
				break;
		}
	}
}
