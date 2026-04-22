import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { UsLaxMembershipService } from '@infrastructure/services/uslax-membership.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
	UsLaxMembershipRole,
	UsLaxReconciliationCandidateDto,
	UsLaxReconciliationRowDto,
	RegistrationSearchRequest
} from '@core/api';
import { BatchEmailModalComponent } from '@views/search/registrations/components/batch-email-modal.component';
import { ROLE_ID_PLAYER, ROLE_ID_UNASSIGNED_ADULT, type JobFlagsForTemplates } from '@views/search/registrations/email-templates';

// C# enum generates as `type UsLaxMembershipRole = number` — mirror legible names at call sites.
const MEMBERSHIP_ROLE = { Player: 0, Coach: 1 } as const satisfies Record<'Player' | 'Coach', UsLaxMembershipRole>;

@Component({
	selector: 'app-uslax-membership',
	standalone: true,
	imports: [DatePipe, NgClass, GridAllModule, BatchEmailModalComponent],
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
	readonly showEmailModal = signal(false);

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

	// Inputs for the BatchEmailModal ---------------------------------------------------

	readonly initialTemplateLabel = computed(() =>
		this.isCoachRole()
			? 'Expired / Missing Membership (Coaches)'
			: 'Expired / Missing Membership (Players)'
	);

	readonly emailRegistrationIds = computed<string[]>(() =>
		this.selectedRows().map(r => r.registrationId)
	);

	readonly emailRecipients = computed(() =>
		this.selectedRows()
			.filter(r => !!r.email)
			.map(r => ({ name: `${r.firstName} ${r.lastName}`.trim(), email: r.email! }))
	);

	readonly emailRecipientCount = computed(() => this.emailRecipients().length);

	readonly emailActiveRoleIds = computed<string[]>(() =>
		this.isCoachRole() ? [ROLE_ID_UNASSIGNED_ADULT] : [ROLE_ID_PLAYER]
	);

	/**
	 * Synthetic search request mirroring what the USLax-expired template requires:
	 * expired status + role + active. Satisfies the modal's availability evaluator
	 * so the right template is picked up.
	 */
	readonly emailSearchRequest = computed<RegistrationSearchRequest>(() => ({
		usLaxMembershipStatus: 'expired',
		roleIds: this.emailActiveRoleIds(),
		activeStatuses: ['True']
	}));

	readonly emailJobFlags = computed<JobFlagsForTemplates>(() => ({
		offerPlayerRegsaverInsurance: false,
		offerTeamRegsaverInsurance: false,
		adnArb: false,
		usLaxMembershipValidated: !!this.jobService.currentJob()?.usLaxNumberValidThroughDate
	}));

	readonly canOpenEmail = computed(() => this.emailRecipients().length > 0);

	readonly emailDisabledReason = computed(() => {
		if (this.selectedRows().length === 0) return 'Select one or more rows to email.';
		if (this.emailRecipients().length === 0) return 'Selected rows have no email address on file.';
		return null;
	});

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
				// Refresh candidate count so the context panel reflects writes.
				this.loadCandidates();
			},
			error: err => {
				this.isReconciling.set(false);
				this.toast.show(`Reconciliation failed: ${err?.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	// Grid selection -------------------------------------------------------------------

	/** Called from rowSelected / rowDeselected — the grid instance is passed
	 *  through from the template (#grid local ref) since the grid is inside an
	 *  @if block and ViewChild can resolve late. */
	onSelectionChanged(grid: GridComponent): void {
		this.gridRef = grid;
		const selected = (grid.getSelectedRecords() as UsLaxReconciliationRowDto[]) ?? [];
		this.selectedRows.set([...selected]);
	}

	// Email ---------------------------------------------------------------------------

	openEmail(): void {
		if (!this.canOpenEmail()) return;
		this.showEmailModal.set(true);
	}

	closeEmail(): void {
		this.showEmailModal.set(false);
	}

	onEmailSent(): void {
		this.showEmailModal.set(false);
		this.selectedRows.set([]);
		this.gridRef?.clearSelection();
	}

	// Grid formatting helpers ---------------------------------------------------------

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
			case 'Name':
				args.value = `${d.lastName}, ${d.firstName}`;
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
