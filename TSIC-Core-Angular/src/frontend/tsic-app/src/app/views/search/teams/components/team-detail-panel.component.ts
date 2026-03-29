import { Component, ChangeDetectionStrategy, input, output, signal, inject, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, AccountingRecordDto, ClubTeamSummaryDto, EditTeamRequest, CreditCardInfo, ClubRegistrationDto, EditAccountingRecordRequest } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { CcChargeModalComponent } from './cc-charge-modal.component';
import { CheckPaymentModalComponent } from './check-payment-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';

type TabType = 'info' | 'accounting';
type Scope = 'team' | 'club';

/** Formats a 10-digit phone string as xxx-xxx-xxxx. */
function formatPhone(value: string | null | undefined): string | null {
	if (!value) return value ?? null;
	const digits = value.replace(/\D/g, '');
	if (digits.length === 10) return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
	return value;
}

@Component({
	selector: 'app-team-detail-panel',
	standalone: true,
	imports: [CommonModule, FormsModule, CcChargeModalComponent, CheckPaymentModalComponent, ConfirmDialogComponent],
	templateUrl: './team-detail-panel.component.html',
	styleUrl: './team-detail-panel.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamDetailPanelComponent {
	detail = input<TeamSearchDetailDto | null>(null);
	isOpen = input<boolean>(false);

	closed = output<void>();
	changed = output<void>();

	private readonly searchService = inject(TeamSearchService);
	private readonly toast = inject(ToastService);

	activeTab = linkedSignal({ source: () => this.detail(), computation: () => 'info' as TabType });
	scope = linkedSignal({ source: () => this.detail(), computation: () => 'team' as Scope });

	// Edit state — reset from detail when it changes
	editTeamName = linkedSignal(() => this.detail()?.teamName ?? '');
	editActive = linkedSignal(() => this.detail()?.active ?? true);
	editLevelOfPlay = linkedSignal(() => this.detail()?.levelOfPlay ?? '');
	editComments = linkedSignal(() => this.detail()?.teamComments ?? '');
	isSaving = signal(false);

	// Modals
	showCcChargeModal = signal(false);
	showCheckModal = signal(false);
	checkModalType = signal<'Check' | 'Correction'>('Check');

	// Active toggle (header)
	isTogglingActive = signal(false);

	// Refund confirm
	showRefundConfirm = signal(false);
	refundTarget = signal<AccountingRecordDto | null>(null);

	// Inline editing for accounting records
	editingAId = signal<number | null>(null);
	editComment = signal('');
	editCheckNo = signal('');
	isSavingEdit = signal(false);

	// Move team confirm
	showMoveTeamConfirm = signal(false);

	// Club rep operations
	showChangeClub = signal(false);
	showTransferAll = signal(false);
	clubRegistrations = signal<ClubRegistrationDto[]>([]);
	selectedTargetRegId = signal('');
	transferTargetRegId = signal('');
	isMoving = signal(false);
	isTransferring = signal(false);
	showTransferConfirm = signal(false);

	@HostListener('document:keydown.escape')
	onEscapeKey(): void {
		if (this.isOpen()) { this.close(); }
	}

	close(): void {
		this.closed.emit();
	}

	/** Format phone for display */
	formatPhone(value: string | null | undefined): string | null {
		return formatPhone(value);
	}

	setActiveTab(tab: TabType): void {
		this.activeTab.set(tab);
	}

	setScope(s: Scope): void {
		this.scope.set(s);
	}

	// ── Financial summaries ──

	get teamFeeTotal(): number {
		return this.detail()?.feeTotal ?? 0;
	}

	get teamPaidTotal(): number {
		return this.detail()?.paidTotal ?? 0;
	}

	get teamOwedTotal(): number {
		return this.detail()?.owedTotal ?? 0;
	}

	get clubFeeTotal(): number {
		return this.detail()?.clubTeamSummaries?.reduce((s, t) => s + t.feeTotal, 0) ?? 0;
	}

	get clubPaidTotal(): number {
		return this.detail()?.clubTeamSummaries?.reduce((s, t) => s + t.paidTotal, 0) ?? 0;
	}

	get clubOwedTotal(): number {
		return this.detail()?.clubTeamSummaries?.reduce((s, t) => s + t.owedTotal, 0) ?? 0;
	}

	get clubTeamCount(): number {
		return this.detail()?.clubTeamSummaries?.length ?? 0;
	}

	get scopeLabel(): string {
		const d = this.detail();
		if (!d) return '';
		return this.scope() === 'team' ? d.teamName : (d.clubName ?? 'All Club Teams');
	}

	// ── Edit ──

	saveTeamInfo(): void {
		const d = this.detail();
		if (!d) return;

		this.isSaving.set(true);
		const req: EditTeamRequest = {
			teamName: this.editTeamName() || undefined,
			active: this.editActive(),
			levelOfPlay: this.editLevelOfPlay() || undefined,
			teamComments: this.editComments() || undefined
		};

		this.searchService.editTeam(d.teamId, req).subscribe({
			next: () => {
				this.toast.show('Team updated', 'success', 3000);
				this.isSaving.set(false);
				this.changed.emit();
			},
			error: (err) => {
				this.toast.show('Failed to update team', 'danger', 4000);
				console.error('Edit error:', err);
				this.isSaving.set(false);
			}
		});
	}

	// ── Active Toggle (header) ──

	toggleActive(): void {
		const d = this.detail();
		if (!d) return;

		this.isTogglingActive.set(true);
		const req: EditTeamRequest = {
			teamName: d.teamName,
			active: !d.active,
			levelOfPlay: d.levelOfPlay || undefined,
			teamComments: d.teamComments || undefined
		};

		this.searchService.editTeam(d.teamId, req).subscribe({
			next: () => {
				(d as Record<string, unknown>)['active'] = !d.active;
				this.isTogglingActive.set(false);
				this.editActive.set(d.active);
				this.toast.show(d.active ? 'Team activated' : 'Team deactivated', 'success', 3000);
				this.changed.emit();
			},
			error: (err) => {
				this.isTogglingActive.set(false);
				this.toast.show('Failed to update: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
			}
		});
	}

	// ── Inline Editing (Accounting Records) ──

	startEditRecord(record: AccountingRecordDto): void {
		this.editingAId.set(record.aId);
		this.editComment.set(record.comment || '');
		this.editCheckNo.set(record.checkNo || '');
	}

	cancelEditRecord(): void {
		this.editingAId.set(null);
	}

	saveEditRecord(): void {
		const aId = this.editingAId();
		if (aId == null) return;

		this.isSavingEdit.set(true);
		this.searchService.editAccountingRecord(aId, {
			comment: this.editComment() || null,
			checkNo: this.editCheckNo() || null
		}).subscribe({
			next: () => {
				this.isSavingEdit.set(false);
				this.editingAId.set(null);
				this.toast.show('Record updated', 'success', 3000);
				this.changed.emit();
			},
			error: (err) => {
				this.isSavingEdit.set(false);
				this.toast.show('Failed to update: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
			}
		});
	}

	/** Check/Correction/Cash records are editable (not CC records). */
	isEditable(record: AccountingRecordDto): boolean {
		const method = (record.paymentMethod || '').toLowerCase();
		return method.includes('check') || method.includes('correction') || method.includes('cash');
	}

	// ── Modals ──

	openCcCharge(): void {
		this.showCcChargeModal.set(true);
	}

	openCheckPayment(): void {
		this.checkModalType.set('Check');
		this.showCheckModal.set(true);
	}

	openCorrection(): void {
		this.checkModalType.set('Correction');
		this.showCheckModal.set(true);
	}

	onRefundRequested(record: AccountingRecordDto): void {
		if (!record.aId) return;
		this.refundTarget.set(record);
		this.showRefundConfirm.set(true);
	}

	onRefundConfirmed(): void {
		const record = this.refundTarget();
		this.showRefundConfirm.set(false);
		this.refundTarget.set(null);
		if (!record?.aId) return;

		this.searchService.processRefund({
			accountingRecordId: record.aId,
			refundAmount: record.paidAmount ?? 0,
			reason: 'Admin refund from team search'
		}).subscribe({
			next: (result) => {
				if (result.success) {
					this.toast.show('Refund processed', 'success', 4000);
					this.changed.emit();
				} else {
					this.toast.show(result.message ?? 'Refund failed', 'danger', 4000);
				}
			},
			error: (err) => {
				this.toast.show('Refund failed', 'danger', 4000);
				console.error('Refund error:', err);
			}
		});
	}

	onRefundCancelled(): void {
		this.showRefundConfirm.set(false);
		this.refundTarget.set(null);
	}

	onCcChargeComplete(): void {
		this.showCcChargeModal.set(false);
		this.changed.emit();
	}

	onCheckPaymentComplete(): void {
		this.showCheckModal.set(false);
		this.changed.emit();
	}

	// ── Club Rep Operations ──

	openChangeClub(): void {
		this.cancelClubOps();
		this.searchService.getClubRegistrations().subscribe({
			next: (clubs) => {
				const currentRegId = this.detail()?.clubRepRegistrationId;
				this.clubRegistrations.set(clubs.filter(c => c.registrationId !== currentRegId));
				this.showChangeClub.set(true);
			},
			error: () => this.toast.show('Failed to load club list', 'danger', 4000)
		});
	}

	confirmChangeClub(): void {
		if (!this.selectedTargetRegId()) return;
		this.showMoveTeamConfirm.set(true);
	}

	doChangeClub(): void {
		const d = this.detail();
		const targetId = this.selectedTargetRegId();
		if (!d || !targetId) return;

		this.showMoveTeamConfirm.set(false);
		this.isMoving.set(true);
		this.searchService.changeClub(d.teamId, { targetRegistrationId: targetId }).subscribe({
			next: (result) => {
				this.isMoving.set(false);
				this.cancelClubOps();
				this.toast.show(result.message, 'success', 4000);
				this.changed.emit();
			},
			error: (err) => {
				this.isMoving.set(false);
				this.toast.show(err.error?.message || 'Failed to change club', 'danger', 4000);
			}
		});
	}

	openTransferAll(): void {
		this.cancelClubOps();
		this.searchService.getClubRegistrations().subscribe({
			next: (clubs) => {
				const currentRegId = this.detail()?.clubRepRegistrationId;
				this.clubRegistrations.set(clubs.filter(c => c.registrationId !== currentRegId));
				this.showTransferAll.set(true);
			},
			error: () => this.toast.show('Failed to load club list', 'danger', 4000)
		});
	}

	confirmTransferAll(): void {
		if (!this.transferTargetRegId()) return;
		this.showTransferConfirm.set(true);
	}

	doTransferAll(): void {
		const d = this.detail();
		const targetId = this.transferTargetRegId();
		if (!d?.clubRepRegistrationId || !targetId) return;

		this.showTransferConfirm.set(false);
		this.isTransferring.set(true);
		this.searchService.transferAllTeams({
			sourceRegistrationId: d.clubRepRegistrationId,
			targetRegistrationId: targetId
		}).subscribe({
			next: (result) => {
				this.isTransferring.set(false);
				this.cancelClubOps();
				this.toast.show(result.message, 'success', 5000);
				this.changed.emit();
			},
			error: (err) => {
				this.isTransferring.set(false);
				this.toast.show(err.error?.message || 'Transfer failed', 'danger', 4000);
			}
		});
	}

	cancelClubOps(): void {
		this.showChangeClub.set(false);
		this.showTransferAll.set(false);
		this.showTransferConfirm.set(false);
		this.selectedTargetRegId.set('');
		this.transferTargetRegId.set('');
		this.clubRegistrations.set([]);
	}
}
