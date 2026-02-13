import { Component, ChangeDetectionStrategy, input, output, signal, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, AccountingRecordDto, ClubTeamSummaryDto, EditTeamRequest, CreditCardInfo } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { CcChargeModalComponent } from './cc-charge-modal.component';
import { CheckPaymentModalComponent } from './check-payment-modal.component';

type TabType = 'info' | 'accounting';
type Scope = 'team' | 'club';

@Component({
	selector: 'app-team-detail-panel',
	standalone: true,
	imports: [CommonModule, FormsModule, CcChargeModalComponent, CheckPaymentModalComponent],
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

	activeTab = signal<TabType>('info');
	scope = signal<Scope>('team');

	// Edit state
	editTeamName = signal('');
	editActive = signal(true);
	editLevelOfPlay = signal('');
	editComments = signal('');
	isSaving = signal(false);

	// Modals
	showCcChargeModal = signal(false);
	showCheckModal = signal(false);
	checkModalType = signal<'Check' | 'Correction'>('Check');

	constructor() {
		effect(() => {
			const d = this.detail();
			if (d) {
				this.editTeamName.set(d.teamName ?? '');
				this.editActive.set(d.active);
				this.editLevelOfPlay.set(d.levelOfPlay ?? '');
				this.editComments.set(d.teamComments ?? '');
				this.scope.set('team');
				this.activeTab.set('info');
			}
		});
	}

	close(): void {
		this.closed.emit();
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
		const confirmed = confirm(`Refund $${record.paidAmount?.toFixed(2)} from transaction ${record.adnTransactionId}?`);
		if (!confirmed) return;

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

	onCcChargeComplete(): void {
		this.showCcChargeModal.set(false);
		this.changed.emit();
	}

	onCheckPaymentComplete(): void {
		this.showCheckModal.set(false);
		this.changed.emit();
	}
}
