import { Component, ChangeDetectionStrategy, input, output, signal, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TeamSearchService } from '../../../views/search/teams/services/team-search.service';
import { RegisteredTeamsGridComponent } from '../../../views/registration/team/components/registered-teams-grid.component';
import { ToastService } from '@shared-ui/toast.service';
import { AccountingLedgerComponent, CcChargeEvent, CheckOrCorrectionEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import { RefundEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import type { ClubRepAccountingDto, RegisteredTeamDto, RefundResponse, TeamPaymentResultDto } from '@core/api';

type Scope = 'team' | 'club';

@Component({
  selector: 'app-club-rep-payment',
  standalone: true,
  imports: [CommonModule, AccountingLedgerComponent, RegisteredTeamsGridComponent],
  templateUrl: './club-rep-payment.component.html',
  styleUrl: './club-rep-payment.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClubRepPaymentComponent {
  /** The club rep's registration ID (required) */
  clubRepRegistrationId = input.required<string>();

  /** Optional: pre-select a specific team (from search/teams entry point) */
  teamId = input<string | undefined>(undefined);

  /** Emitted after any payment/refund succeeds — parent should refresh */
  paymentComplete = output<void>();

  private readonly searchService = inject(TeamSearchService);
  private readonly toast = inject(ToastService);

  // Data
  data = signal<ClubRepAccountingDto | null>(null);
  isLoading = signal(false);
  loadError = signal<string | null>(null);

  // Per-team CC charge outcomes — set on a partial-success or all-failed club/team charge so the
  // admin sees which teams charged and which declined (and why). Null until then.
  chargeResult = signal<TeamPaymentResultDto[] | null>(null);

  // Scope
  scope = signal<Scope>('club');

  // Refund (handled by accounting-ledger modal now)

  // Reveal toggle for the muted waitlist/dropped/inactive bucket (collapsed by default).
  showOtherTeams = signal(false);

  // Computed summaries — all teams, split into scheduled vs other
  allTeams = computed(() => this.data()?.teams ?? []);
  scheduledTeams = computed(() => this.allTeams().filter(t => t.active
    && !t.ageGroupName.toUpperCase().startsWith('WAITLIST')
    && !t.ageGroupName.toUpperCase().startsWith('DROPPED')));
  otherTeams = computed(() => this.allTeams().filter(t => !t.active
    || t.ageGroupName.toUpperCase().startsWith('WAITLIST')
    || t.ageGroupName.toUpperCase().startsWith('DROPPED')));
  clubTeamCount = computed(() => this.allTeams().length);

  // Teams fed to the rich grid: active scheduled teams in club scope, the single
  // selected team in team scope. Totals (active-only) are preserved by sourcing the
  // grid + ledger from scheduledTeams, never the full set.
  gridTeams = computed<RegisteredTeamDto[]>(() => {
    if (this.scope() === 'team') {
      const t = this.selectedTeam();
      return t ? [t] : [];
    }
    return this.scheduledTeams();
  });

  clubFeeTotal = computed(() => this.scheduledTeams().reduce((s, t) => s + t.feeTotal, 0));
  clubPaidTotal = computed(() => this.scheduledTeams().reduce((s, t) => s + t.paidTotal, 0));
  clubOwedTotal = computed(() => this.scheduledTeams().reduce((s, t) => s + t.owedTotal, 0));

  selectedTeam = computed(() => {
    const tid = this.teamId();
    if (!tid) return null;
    return this.allTeams().find(t => t.teamId === tid) ?? null;
  });

  teamFeeTotal = computed(() => this.selectedTeam()?.feeTotal ?? 0);
  teamPaidTotal = computed(() => this.selectedTeam()?.paidTotal ?? 0);
  teamOwedTotal = computed(() => this.selectedTeam()?.owedTotal ?? 0);

  feeTotal = computed(() => this.scope() === 'team' ? this.teamFeeTotal() : this.clubFeeTotal());
  paidTotal = computed(() => this.scope() === 'team' ? this.teamPaidTotal() : this.clubPaidTotal());
  owedTotal = computed(() => this.scope() === 'team' ? this.teamOwedTotal() : this.clubOwedTotal());

  // Check/correction owed — the canonical CkOwedTotal per team (from the backend
  // resolver), scoped exactly like owedTotal.
  teamCheckOwed = computed(() => this.selectedTeam()?.ckOwedTotal ?? 0);
  clubCheckOwed = computed(() => this.scheduledTeams().reduce((s, t) => s + t.ckOwedTotal, 0));
  checkOwed = computed(() => this.scope() === 'team' ? this.teamCheckOwed() : this.clubCheckOwed());

  clubBreakdown = computed<RegisteredTeamDto[] | undefined>(() =>
    this.scope() === 'club' ? this.allTeams() : undefined
  );

  // CC-only jobs (Jobs.PaymentMethodsAllowedCode === 1) can never take a check, so the
  // "Check Owed" column in the breakdown grid is pure noise — drop it. Missing code defaults
  // server-side to CC-or-Check, so the column shows unless the job is explicitly CC-only.
  ccOnly = computed(() => this.data()?.paymentMethodsAllowedCode === 1);

  allAccountingRecords = computed(() => this.data()?.accountingRecords ?? []);
  accountingRecords = computed(() => {
    const records = this.allAccountingRecords();
    if (this.scope() !== 'team') return records;
    const tid = this.teamId();
    if (!tid) return records;
    return records.filter(r => r.teamId === tid);
  });
  clubName = computed(() => this.data()?.clubName ?? '');

  scopeLabel = computed(() => {
    if (this.scope() === 'team') {
      return this.selectedTeam()?.teamName ?? '';
    }
    return this.clubName() || 'All Club Teams';
  });

  ngOnInit(): void {
    // Default scope: if teamId provided and multiple teams, start on 'team'; otherwise 'club'
    if (this.teamId()) {
      this.scope.set('team');
    }
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.searchService.getClubRepAccounting(this.clubRepRegistrationId()).subscribe({
      next: (dto) => {
        this.data.set(dto);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.loadError.set(err?.error?.message || 'Failed to load club rep accounting');
        this.isLoading.set(false);
      }
    });
  }

  setScope(s: Scope): void {
    this.scope.set(s);
  }

  // ── Accounting Ledger Handlers ──

  onCcCharge(event: CcChargeEvent): void {
    const regId = this.clubRepRegistrationId();
    const tid = this.teamId();
    const request = { clubRepRegistrationId: regId, creditCard: event.creditCard };
    this.chargeResult.set(null);

    const call = (this.scope() === 'team' && tid)
      ? this.searchService.chargeCcForTeam(tid, request)
      : this.searchService.chargeCcForClub(regId, request);

    call.subscribe({
      next: (result) => {
        if (result.success) {
          this.toast.show(`$${event.amount.toFixed(2)} charged successfully`, 'success', 3000, 'CC Charge');
          this.loadData();
          this.paymentComplete.emit();
        } else {
          // Partial-success or all-failed: the engine already persisted the teams that cleared.
          // Surface the per-team outcomes AND refresh the grid/totals so they reflect the real
          // remaining balance, then let the admin retry only the declined team(s).
          this.toast.show(result.error || 'Unknown error', 'danger', 0, 'CC Charge Failed');
          const teams = (result.teams ?? []) as TeamPaymentResultDto[];
          this.chargeResult.set(teams.length > 0 ? teams : null);
          this.loadData();
        }
      },
      error: (err) => {
        this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, 'CC Charge Failed');
      }
    });
  }

  onCheckSubmitted(event: CheckOrCorrectionEvent): void {
    const regId = this.clubRepRegistrationId();
    const tid = this.teamId();
    const label = event.paymentType === 'Check' ? 'Check Payment' : 'Correction';
    const request = {
      clubRepRegistrationId: regId,
      amount: event.amount,
      checkNo: event.checkNo || undefined,
      comment: event.comment || undefined,
      paymentType: event.paymentType
    };

    const call = (this.scope() === 'team' && tid)
      ? this.searchService.recordCheckForTeam(tid, request)
      : this.searchService.recordCheckForClub(regId, request);

    call.subscribe({
      next: (result) => {
        if (result.success) {
          this.toast.show(`$${event.amount.toFixed(2)} recorded`, 'success', 3000, label);
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(result.error || 'Unknown error', 'danger', 0, `${label} Failed`);
        }
      },
      error: (err) => {
        this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, `${label} Failed`);
      }
    });
  }

  onRefundSubmitted(event: RefundEvent): void {
    this.searchService.processRefund({
      accountingRecordId: event.accountingRecordId,
      refundAmount: event.refundAmount,
      reason: 'Admin refund from club rep payment'
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
      error: (err) => {
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'CC Refund Failed');
      }
    });
  }
}
