import { Component, ChangeDetectionStrategy, input, output, signal, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TeamSearchService } from '../../../views/search/teams/services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { AccountingLedgerComponent, CcChargeEvent, CheckOrCorrectionEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import { RefundEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import type { ClubRepAccountingDto, AccountingRecordDto, ClubTeamSummaryDto, RefundResponse } from '@core/api';

type Scope = 'team' | 'club';

@Component({
  selector: 'app-club-rep-payment',
  standalone: true,
  imports: [CommonModule, AccountingLedgerComponent],
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

  // Scope
  scope = signal<Scope>('club');

  // Refund (handled by accounting-ledger modal now)

  // Computed summaries — all teams, split into scheduled vs other
  allTeams = computed(() => this.data()?.teams ?? []);
  scheduledTeams = computed(() => this.allTeams().filter(t => t.active
    && !t.agegroupName.toUpperCase().startsWith('WAITLIST')
    && !t.agegroupName.toUpperCase().startsWith('DROPPED')));
  otherTeams = computed(() => this.allTeams().filter(t => !t.active
    || t.agegroupName.toUpperCase().startsWith('WAITLIST')
    || t.agegroupName.toUpperCase().startsWith('DROPPED')));
  clubTeamCount = computed(() => this.allTeams().length);

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

  clubBreakdown = computed<ClubTeamSummaryDto[] | undefined>(() =>
    this.scope() === 'club' ? this.allTeams() : undefined
  );

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

    const call = (this.scope() === 'team' && tid)
      ? this.searchService.chargeCcForTeam(tid, request)
      : this.searchService.chargeCcForClub(regId, request);

    call.subscribe({
      next: (result) => {
        if (result.success) {
          this.toast.show(`CC charge successful: $${event.amount.toFixed(2)}`, 'success', 3000);
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(`CC charge failed: ${result.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => {
        this.toast.show(`CC charge failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000);
      }
    });
  }

  onCheckSubmitted(event: CheckOrCorrectionEvent): void {
    const regId = this.clubRepRegistrationId();
    const tid = this.teamId();
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
          this.toast.show(`${event.paymentType} recorded: $${event.amount.toFixed(2)}`, 'success', 3000);
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(`Failed: ${result.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => {
        this.toast.show(`Failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000);
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
          this.toast.show(`Refund of $${event.refundAmount.toFixed(2)} processed`, 'success', 4000);
          this.loadData();
          this.paymentComplete.emit();
        } else {
          this.toast.show(result.message ?? 'Refund failed', 'danger', 0);
        }
      },
      error: (err) => {
        const msg = err?.error?.message || 'Refund failed — unknown error';
        this.toast.show(msg, 'danger', 0);
      }
    });
  }
}
