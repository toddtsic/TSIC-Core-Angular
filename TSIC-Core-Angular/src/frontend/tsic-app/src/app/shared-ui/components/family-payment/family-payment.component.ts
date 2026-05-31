import { Component, ChangeDetectionStrategy, input, output, signal, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationSearchService } from '../../../views/search/registrations/services/registration-search.service';
import { RegisteredTeamsGridComponent } from '../../../views/registration/team/components/registered-teams-grid.component';
import { ToastService } from '@shared-ui/toast.service';
import {
  AccountingLedgerComponent,
  CcChargeEvent,
  CheckOrCorrectionEvent,
  RefundEvent,
  LedgerGroup
} from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import type { FamilyAccountingDto, RegisteredTeamDto, RefundResponse } from '@core/api';

type Scope = 'player' | 'family';

/**
 * Family accounting view — the parent-side analog of app-club-rep-payment. Shows a combined
 * ledger across every player a parent registered for the job (keyed server-side by JobId +
 * FamilyUserId), with a scope selector (this player / all family players) and a per-child
 * breakdown that reuses the SAME Syncfusion registered-teams-grid the club-rep view uses
 * (each child shaped as a RegisteredTeamDto). Reuses the shared AccountingLedgerComponent.
 *
 * Payment actions target the anchor player (the registration whose panel is open). The
 * aggregated family scope is read-mostly: per-row refunds work, but family-wide charge-all
 * is a fast-follow, so the ledger's add-record button is hidden there.
 */
@Component({
  selector: 'app-family-payment',
  standalone: true,
  imports: [CommonModule, AccountingLedgerComponent, RegisteredTeamsGridComponent],
  templateUrl: './family-payment.component.html',
  styleUrl: './family-payment.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FamilyPaymentComponent {
  /** The player registration the detail panel was opened on (the anchor / "this player"). */
  registrationId = input.required<string>();

  /** Emitted after any payment/refund succeeds — host should refresh. */
  paymentComplete = output<void>();

  private readonly searchService = inject(RegistrationSearchService);
  private readonly toast = inject(ToastService);

  data = signal<FamilyAccountingDto | null>(null);
  isLoading = signal(false);
  loadError = signal<string | null>(null);

  scope = signal<Scope>('family');
  showInactive = signal(false);

  // Which player the payment actions + per-player ("This Player") scope target. Defaults to the
  // anchor registration the panel opened on; the scope selector re-points it so an admin can
  // record a check/correction/charge for ANY family player (refunds were already per-row).
  private readonly _activePlayerId = signal<string | null>(null);
  activePlayerId = computed(() => this._activePlayerId() ?? this.registrationId());

  // Each "player" row is a RegisteredTeamDto (TeamId = the child's registrationId).
  allPlayers = computed<RegisteredTeamDto[]>(() => this.data()?.players ?? []);
  activePlayers = computed(() => this.allPlayers().filter(p => p.active));
  inactivePlayers = computed(() => this.allPlayers().filter(p => !p.active));
  playerCount = computed(() => this.allPlayers().length);

  selectedPlayer = computed(() =>
    this.allPlayers().find(p => p.teamId === this.activePlayerId()) ?? null);

  // Grid feed: active players in family scope, the anchor player in player scope.
  gridPlayers = computed<RegisteredTeamDto[]>(() => {
    if (this.scope() === 'player') {
      const p = this.selectedPlayer();
      return p ? [p] : [];
    }
    return this.activePlayers();
  });

  familyName = computed(() => this.data()?.familyName ?? '');

  // Deposit/Balance columns only make sense when the player fees actually carry a deposit
  // (most don't). Conditional, mirroring how the grid auto-shows Discount/Fee-Adj.
  hasDeposit = computed(() => this.allPlayers().some(p => p.deposit > 0));

  // Family totals (active players only — matches the club-rep "scheduled teams" rule).
  familyFeeTotal = computed(() => this.activePlayers().reduce((s, p) => s + p.feeTotal, 0));
  familyPaidTotal = computed(() => this.activePlayers().reduce((s, p) => s + p.paidTotal, 0));
  familyOwedTotal = computed(() => this.activePlayers().reduce((s, p) => s + p.owedTotal, 0));
  familyCheckOwed = computed(() => this.activePlayers().reduce((s, p) => s + p.ckOwedTotal, 0));

  // Scope-resolved summary fed to the ledger. Player scope mirrors the anchor child exactly,
  // so the payment modal's balance-due is correct for the registration we charge.
  feeTotal = computed(() => this.scope() === 'player' ? (this.selectedPlayer()?.feeTotal ?? 0) : this.familyFeeTotal());
  paidTotal = computed(() => this.scope() === 'player' ? (this.selectedPlayer()?.paidTotal ?? 0) : this.familyPaidTotal());
  owedTotal = computed(() => this.scope() === 'player' ? (this.selectedPlayer()?.owedTotal ?? 0) : this.familyOwedTotal());
  checkOwed = computed(() => this.scope() === 'player' ? (this.selectedPlayer()?.ckOwedTotal ?? 0) : this.familyCheckOwed());

  // Neutral groups for the ledger's bucketing + per-row attribution (one per child;
  // key = TeamId = the child's registrationId, matching each record's ownerRegistrationId).
  ledgerGroups = computed<LedgerGroup[]>(() => this.allPlayers().map(p => ({
    key: p.teamId,
    label: p.teamName,
    active: p.active
  })));

  allRecords = computed(() => this.data()?.accountingRecords ?? []);
  accountingRecords = computed(() => {
    if (this.scope() !== 'player') return this.allRecords();
    const id = this.activePlayerId();
    return this.allRecords().filter(r => r.ownerRegistrationId === id);
  });

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.searchService.getFamilyAccounting(this.registrationId()).subscribe({
      next: (dto) => {
        this.data.set(dto);
        // A single-child family has no scope selector — behave like the single-player view
        // (add enabled, anchor totals) instead of being stuck in the read-mostly family scope.
        if ((dto.players?.length ?? 0) <= 1) this.scope.set('player');
        this.isLoading.set(false);
      },
      error: (err) => {
        this.loadError.set(err?.error?.message || 'Failed to load family accounting');
        this.isLoading.set(false);
      }
    });
  }

  setScope(s: Scope): void {
    this.scope.set(s);
  }

  /** Re-point the payment actions + per-player view at a specific family player. */
  selectPlayer(playerId: string): void {
    this._activePlayerId.set(playerId);
    this.scope.set('player');
  }

  // ── Accounting Ledger Handlers (target the active player — see activePlayerId) ──

  onCcCharge(event: CcChargeEvent): void {
    const id = this.activePlayerId();
    this.searchService.chargeCc(id, {
      registrationId: id,
      creditCard: event.creditCard,
      amount: event.amount
    }).subscribe({
      next: (response) => {
        if (response.success) {
          this.toast.show(`$${event.amount.toFixed(2)} charged successfully`, 'success', 3000, 'CC Charge');
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(response.error || 'Unknown error', 'danger', 0, 'CC Charge Failed');
        }
      },
      error: (err) => { this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, 'CC Charge Failed'); }
    });
  }

  onCheckSubmitted(event: CheckOrCorrectionEvent): void {
    const id = this.activePlayerId();
    const label = event.paymentType === 'Check' ? 'Check Payment' : 'Correction';
    this.searchService.recordPayment(id, {
      registrationId: id,
      amount: event.amount,
      paymentType: event.paymentType,
      checkNo: event.checkNo ?? undefined,
      comment: event.comment ?? undefined
    }).subscribe({
      next: (response) => {
        if (response.success) {
          this.toast.show(`$${event.amount.toFixed(2)} recorded`, 'success', 3000, label);
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(response.error || 'Unknown error', 'danger', 0, `${label} Failed`);
        }
      },
      error: (err) => { this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, `${label} Failed`); }
    });
  }

  onRefundSubmitted(event: RefundEvent): void {
    this.searchService.processRefund({
      accountingRecordId: event.accountingRecordId,
      refundAmount: event.refundAmount,
      reason: 'Admin refund from family payment'
    }).subscribe({
      next: (result: RefundResponse) => {
        if (result.success) {
          this.toast.show(`$${event.refundAmount.toFixed(2)} refunded`, 'success', 4000, 'CC Refund');
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(result.message ?? 'Unknown error', 'danger', 0, 'CC Refund Failed');
        }
      },
      error: (err) => { this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'CC Refund Failed'); }
    });
  }
}
