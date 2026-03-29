import { Component, ChangeDetectionStrategy, input, output, signal, inject, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, EditTeamRequest, ClubRegistrationDto } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ClubRepPaymentComponent } from '@shared-ui/components/club-rep-payment/club-rep-payment.component';

type TabType = 'info' | 'accounting';

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
	imports: [CommonModule, FormsModule, ConfirmDialogComponent, ClubRepPaymentComponent],
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

	// Edit state — reset from detail when it changes
	editTeamName = linkedSignal(() => this.detail()?.teamName ?? '');
	editActive = linkedSignal(() => this.detail()?.active ?? true);
	editLevelOfPlay = linkedSignal(() => this.detail()?.levelOfPlay ?? '');
	editComments = linkedSignal(() => this.detail()?.teamComments ?? '');
	isSaving = signal(false);

	// Active toggle (header)
	isTogglingActive = signal(false);

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
