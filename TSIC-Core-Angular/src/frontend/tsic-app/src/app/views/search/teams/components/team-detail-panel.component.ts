import { Component, ChangeDetectionStrategy, input, output, signal, computed, inject, linkedSignal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, EditTeamRequest, ClubRegistrationDto, SubscriptionDetailDto } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ClubRepPaymentComponent } from '@shared-ui/components/club-rep-payment/club-rep-payment.component';
import { LOP_CHOICES, normalizeLop } from '@shared/teams/lop-choices';
import { environment } from '@environments/environment';

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

	// Accounting is the default/first tab; resets to it each time a new team opens.
	activeTab = linkedSignal({ source: () => this.detail(), computation: () => 'accounting' as TabType });

	// Edit state — reset from detail when it changes
	editTeamName = linkedSignal(() => this.detail()?.teamName ?? '');
	editActive = linkedSignal(() => this.detail()?.active ?? true);
	// Normalized to the fixed 1–5 scale so a legacy label ('5 (strongest)') shows as
	// its canonical pill ('5'); unmappable junk ('competitive') falls to '' (— Select —).
	editLevelOfPlay = linkedSignal(() => normalizeLop(this.detail()?.levelOfPlay));
	editComments = linkedSignal(() => this.detail()?.teamComments ?? '');
	isSaving = signal(false);

	/** Fixed 1–5 Level-of-Play choices (shared). The edit form's LOP select binds to this,
	 *  not the former per-job jsonOptions `List_Lops`. The stored value is normalized for
	 *  display (`normalizeLop`): a mappable legacy label ('5 (strongest)') shows as its pill
	 *  ('5'); unmappable junk ('competitive') shows unselected. Either way the raw stored
	 *  value is preserved unless the admin actually picks a new one. */
	readonly lopChoices = LOP_CHOICES;

	/** Unsaved edits in the Team Details form — compared signal-to-source so it survives the
	 *  Details/Accounting tab switch (the form unmounts, but these signals persist). Drives the
	 *  Save affordance and the discard-on-close guard. (Active is excluded — its own drop flow
	 *  is an explicit, immediate confirm, not a deferred save.) */
	readonly isDirty = computed(() => {
		const d = this.detail();
		if (!d) return false;
		return (this.editTeamName() ?? '') !== (d.teamName ?? '')
			// Compare against the NORMALIZED original so an unchanged legacy LOP
			// ('5 (strongest)' → '5') doesn't read as dirty the instant the form opens.
			|| this.editLevelOfPlay() !== normalizeLop(d.levelOfPlay)
			|| (this.editComments() ?? '') !== (d.teamComments ?? '');
	});

	/** Discard-changes guard shown when closing with unsaved edits. */
	showDiscardConfirm = signal(false);

	// Active toggle (header)
	isTogglingActive = signal(false);
	showDropTeamConfirm = signal(false);

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
		// Don't let an accidental X / backdrop / Esc silently throw away edits.
		if (this.isDirty()) {
			this.showDiscardConfirm.set(true);
			return;
		}
		this.closed.emit();
	}

	confirmDiscard(): void {
		this.showDiscardConfirm.set(false);
		this.closed.emit();
	}

	cancelDiscard(): void {
		this.showDiscardConfirm.set(false);
	}

	// ── Panel resize ──
	// The panel is anchored to the right, so dragging the left edge LEFT widens it. Width
	// persists per-browser so wide accounting/club content stays as wide as the user set it.
	// Pointer capture routes move/up back to the handle — no document listeners, no effect().
	private static readonly WIDTH_KEY = 'teamDetailPanelWidth';
	private static readonly DEFAULT_WIDTH = 560;
	private static readonly MIN_WIDTH = 480;
	private static readonly MAX_WIDTH = 1100;

	panelWidth = signal<number>(this.readStoredWidth());
	isResizing = signal<boolean>(false);
	private resizeStartX = 0;
	private resizeStartWidth = 0;

	private readStoredWidth(): number {
		try {
			const raw = Number(localStorage.getItem(TeamDetailPanelComponent.WIDTH_KEY));
			if (raw && !Number.isNaN(raw)) return this.clampWidth(raw);
		} catch { /* localStorage unavailable — fall through to default */ }
		return TeamDetailPanelComponent.DEFAULT_WIDTH;
	}

	private clampWidth(w: number): number {
		const max = Math.min(TeamDetailPanelComponent.MAX_WIDTH, Math.round(window.innerWidth * 0.9));
		return Math.max(TeamDetailPanelComponent.MIN_WIDTH, Math.min(max, w));
	}

	startResize(ev: PointerEvent): void {
		ev.preventDefault();
		this.resizeStartX = ev.clientX;
		this.resizeStartWidth = this.panelWidth();
		this.isResizing.set(true);
		(ev.target as HTMLElement).setPointerCapture?.(ev.pointerId);
	}

	onResizeMove(ev: PointerEvent): void {
		if (!this.isResizing()) return;
		// Right-anchored: as the pointer moves left (clientX shrinks), the panel grows.
		const delta = this.resizeStartX - ev.clientX;
		this.panelWidth.set(this.clampWidth(this.resizeStartWidth + delta));
	}

	endResize(ev: PointerEvent): void {
		if (!this.isResizing()) return;
		this.isResizing.set(false);
		(ev.target as HTMLElement).releasePointerCapture?.(ev.pointerId);
		try { localStorage.setItem(TeamDetailPanelComponent.WIDTH_KEY, String(this.panelWidth())); } catch { /* ignore */ }
	}

	resetWidth(): void {
		this.panelWidth.set(TeamDetailPanelComponent.DEFAULT_WIDTH);
		try { localStorage.removeItem(TeamDetailPanelComponent.WIDTH_KEY); } catch { /* ignore */ }
	}

	/** Format phone for display */
	formatPhone(value: string | null | undefined): string | null {
		return formatPhone(value);
	}

	setActiveTab(tab: TabType): void {
		this.activeTab.set(tab);
	}

	// ── ARB Subscription ──
	// Only Production talks to live Authorize.Net; every other host is sandboxed and a prod-origin
	// subscription lives in an account it can't reach, so off-Production we lean on the stored snapshot.
	readonly isProdEnv = environment.envName === 'production';

	// Seeded from the stored snapshot (projected from the team's AdnSubscription* columns) so the
	// badge + card show in EVERY environment with no gateway call. linkedSignal re-seeds when a new
	// team opens; a live Production read overwrites it via .set() until the next team. (No effect() —
	// this is pure derivation, matching the panel's linkedSignal idiom.)
	subscription = linkedSignal<SubscriptionDetailDto | null>(() => this.detail()?.storedSubscription ?? null);
	// True only once a LIVE Authorize.Net read has succeeded (Production). While false, the card shows
	// the stored snapshot — display-only, so Cancel stays hidden. Resets to false on each new team.
	subscriptionIsLive = linkedSignal({ source: () => this.detail(), computation: () => false });
	isLoadingSubscription = signal(false);
	isCancellingSubscription = signal(false);
	showCancelSubConfirm = signal(false);

	// Payment progress for the header ARB badge (x of y occurrences). Derived from paid ÷ per-occurrence,
	// but ONLY when the paid total is a CLEAN multiple of the occurrence amount (uniform ARB) — never
	// infer a count off a deposit / partial / mixed balance, which would misstate money.
	arbProgress = computed<{ paid: number; total: number } | null>(() => {
		const sub = this.subscription();
		const d = this.detail();
		if (!sub || !d) return null;
		const total = sub.totalOccurrences;
		const per = sub.perOccurrenceAmount;
		const paidTotal = d.paidTotal ?? 0;
		if (!total || !per || per <= 0) return null;
		const paid = Math.round(paidTotal / per);
		if (paid < 0 || paid > total || Math.abs(paid * per - paidTotal) > 0.005) return null;
		return { paid, total };
	});

	/** Fetch live Authorize.Net status (Production). Keeps the stored snapshot on failure. */
	loadSubscription(): void {
		const d = this.detail();
		if (!d || !d.hasSubscription) return;

		this.isLoadingSubscription.set(true);
		this.searchService.getSubscription(d.teamId).subscribe({
			next: (sub) => {
				this.subscription.set(sub);
				this.subscriptionIsLive.set(true);
				this.isLoadingSubscription.set(false);
			},
			error: () => {
				// Live refresh failed (e.g. ADN outage / off-Production). Keep the stored snapshot on
				// screen rather than wiping it, and leave it not-live so actions stay gated.
				this.subscriptionIsLive.set(false);
				this.isLoadingSubscription.set(false);
				this.toast.show('Live subscription status unavailable; showing the stored record.', 'warning', 5000);
			}
		});
	}

	confirmCancelSubscription(): void { this.showCancelSubConfirm.set(true); }
	dismissCancelSubscription(): void { this.showCancelSubConfirm.set(false); }

	cancelSubscription(): void {
		const d = this.detail();
		if (!d) return;

		this.showCancelSubConfirm.set(false);
		this.isCancellingSubscription.set(true);
		this.searchService.cancelSubscription(d.teamId).subscribe({
			next: () => {
				this.isCancellingSubscription.set(false);
				this.toast.show('Subscription cancelled', 'success', 3000);
				this.loadSubscription();
				this.changed.emit();
			},
			error: (err) => {
				this.isCancellingSubscription.set(false);
				this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Cancel Subscription Failed');
			}
		});
	}

	// ── Edit ──

	saveTeamInfo(): void {
		const d = this.detail();
		if (!d) return;

		// Only persist LOP when the admin actually changed it (vs the normalized
		// original) — otherwise omit it so saving an unrelated field can't silently
		// rewrite a legacy stored value ('5 (strongest)' → '5'); the backend skips null.
		const lopChanged = this.editLevelOfPlay() !== normalizeLop(d.levelOfPlay);

		this.isSaving.set(true);
		const req: EditTeamRequest = {
			teamName: this.editTeamName() || undefined,
			active: this.editActive(),
			levelOfPlay: lopChanged ? (this.editLevelOfPlay() || undefined) : undefined,
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

		if (d.active) {
			// Deactivating → show drop confirmation
			this.showDropTeamConfirm.set(true);
			return;
		}

		// Reactivating a dropped team — must go through pool assignment
		this.toast.show(
			'To reactivate this team, move it to an agegroup via Pool Assignment (LADT). This ensures fees are calculated and club rep financials updated.',
			'info', 6000
		);
	}

	doDropTeam(): void {
		const d = this.detail();
		if (!d) return;

		this.showDropTeamConfirm.set(false);
		this.isTogglingActive.set(true);

		this.searchService.dropTeam(d.teamId).subscribe({
			next: (result) => {
				this.isTogglingActive.set(false);
				const msg = result.playersAffected > 0
					? `${result.message} (${result.playersAffected} player fee${result.playersAffected === 1 ? '' : 's'} zeroed)`
					: result.message;
				this.toast.show(msg, 'success', 5000);
				this.changed.emit();
				this.closed.emit();
			},
			error: (err) => {
				this.isTogglingActive.set(false);
				this.toast.show('Cannot drop team: ' + (err?.error?.message || 'Unknown error'), 'danger', 5000);
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
