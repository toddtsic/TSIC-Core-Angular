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
  LedgerGroup,
  LedgerAddTarget
} from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import type { FamilyAccountingDto, RegisteredTeamDto, RefundResponse } from '@core/api';

type Scope = 'family' | 'person';

/**
 * A player grouped across ALL their registrations in this job. Keyed by name — a child
 * registers under one account, so every registration for that child carries the identical
 * name. Holds the per-registration rows ("events") and their summed owed, so the ledger
 * shows ONE tile per player that scales when a player signs up for many events (tournaments)
 * instead of one tile per registration.
 */
interface PersonGroup {
  name: string;
  active: boolean;               // any registration active
  events: RegisteredTeamDto[];   // one per registration (teamId = registrationId)
  registrationIds: string[];
  owedTotal: number;
}

/**
 * Family accounting view — the parent-side analog of app-club-rep-payment. Shows a combined
 * ledger across every player a parent registered for the job (keyed server-side by JobId +
 * FamilyUserId), with a three-tier scope: All Family → one tile per player (summed) → a single
 * event (registration) of that player.
 *
 * Selecting a player reveals the combined ledger of ALL their records regardless of team
 * (read-only). Money always attaches to ONE registration, so adding a payment / charging a
 * card requires picking a specific event first; the per-record ag:team line keeps the combined
 * ledger readable. Event labels are derived from the records already on the client (each
 * carries OwnerTeamName / OwnerAgeGroupName) — no extra backend round-trip.
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
  /** The player registration the detail panel was opened on (the anchor). */
  registrationId = input.required<string>();

  /** Emitted after any payment/refund succeeds — host should refresh. */
  paymentComplete = output<void>();

  private readonly searchService = inject(RegistrationSearchService);
  private readonly toast = inject(ToastService);

  data = signal<FamilyAccountingDto | null>(null);
  isLoading = signal(false);
  loadError = signal<string | null>(null);

  scope = signal<Scope>('family');

  // Which player (by name) the person scope is viewing.
  activePersonName = signal<string | null>(null);
  // The registration the in-flight add targets — set when the ledger's add-record picker chooses
  // an event (or auto-selects the lone event of a single-event player). Read only at submit time;
  // it does NOT filter the (combined) ledger view.
  activeEventRegId = signal<string | null>(null);

  // First load seeds the opening scope; later reloads (after a payment) preserve the user's place.
  private initialized = false;

  // Each registration row is a RegisteredTeamDto (teamId = registrationId, teamName = player name).
  allPlayers = computed<RegisteredTeamDto[]>(() => this.data()?.players ?? []);
  allRecords = computed(() => this.data()?.accountingRecords ?? []);

  // registrationId → "AgeGroup · TeamName", read off that registration's records (each record
  // carries OwnerTeamName / OwnerAgeGroupName). The label for the EVENT a registration belongs
  // to, used to disambiguate one player's many registrations. Absent for a registration with no
  // records yet (callers fall back to the reg date).
  private eventLabels = computed<Map<string, string>>(() => {
    const map = new Map<string, string>();
    for (const r of this.allRecords()) {
      const regId = r.ownerRegistrationId;
      if (!regId || map.has(regId)) continue;
      const team = r.ownerTeamName?.trim();
      if (!team) continue;
      const ageGroup = r.ownerAgeGroupName?.trim();
      map.set(regId, ageGroup ? `${ageGroup} · ${team}` : team);
    }
    return map;
  });

  // Players grouped by name — one tile per person, summed across their registrations.
  persons = computed<PersonGroup[]>(() => {
    const groups = new Map<string, PersonGroup>();
    for (const p of this.allPlayers()) {
      let g = groups.get(p.teamName);
      if (!g) {
        g = { name: p.teamName, active: false, events: [], registrationIds: [], owedTotal: 0 };
        groups.set(p.teamName, g);
      }
      g.events.push(p);
      g.registrationIds.push(p.teamId);
      g.active ||= p.active;
      g.owedTotal += p.owedTotal;
    }
    // Active-first, then by name for a stable order.
    return [...groups.values()].sort((a, b) => Number(b.active) - Number(a.active) || a.name.localeCompare(b.name));
  });

  personCount = computed(() => this.persons().length);

  // Names that occur on more than one registration — only these need an event label in the
  // family overview grid (a player with a single registration is already unambiguous).
  private duplicatedNames = computed(() => {
    const counts = new Map<string, number>();
    for (const p of this.allPlayers()) counts.set(p.teamName, (counts.get(p.teamName) ?? 0) + 1);
    return new Set([...counts].filter(([, n]) => n > 1).map(([name]) => name));
  });

  activePerson = computed<PersonGroup | null>(() =>
    this.persons().find(p => p.name === this.activePersonName()) ?? null);

  // Add records only in person scope (the family overview is read-only — a family-wide charge is
  // a fast-follow). Refunds remain per-row everywhere.
  canAddRecord = computed(() => this.scope() === 'person');

  // Registrations a new record can attach to — the viewed player's events, each carrying its OWN
  // balances so the modal's amount caps bind to the picked event, not the player's combined total.
  // A record-less event is still listed (it has no ledger row but is a valid charge target — the
  // gap the old in-ledger row-click couldn't reach). One event → the ledger auto-targets it with
  // no picker; many → the modal asks which first.
  addTargets = computed<LedgerAddTarget[]>(() => {
    if (this.scope() !== 'person') return [];
    const person = this.activePerson();
    if (!person) return [];
    return person.events.map(e => ({
      key: e.teamId,
      label: this.eventLabels().get(e.teamId) ?? `Registered ${this.shortDate(e.registrationTs)}`,
      owed: e.owedTotal,
      checkOwed: e.ckOwedTotal,
      paid: e.paidTotal
    }));
  });

  /** M/d/yy for a registration date, used to label a record-less event in the picker. */
  private shortDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return `${d.getMonth() + 1}/${d.getDate()}/${String(d.getFullYear()).slice(-2)}`;
  }

  // Stamp the event label onto a row's name. force=true (player scope) always relabels to the
  // event; force=false (family overview) only relabels when the player's name is duplicated.
  private withEventLabels(rows: RegisteredTeamDto[], force: boolean): RegisteredTeamDto[] {
    const dups = this.duplicatedNames();
    return rows.map(r => {
      if (!force && !dups.has(r.teamName)) return r;
      const label = this.eventLabels().get(r.teamId);
      if (!label) return r; // no event label available → leave the player name
      return { ...r, teamName: force ? label : `${r.teamName} — ${label}` };
    });
  }

  // Breakdown grids: family = Active/Inactive across everyone (event label on duplicated names);
  // player = that player's events, each row named by its event.
  breakdownSections = computed<{ title: string; teams: RegisteredTeamDto[]; teamColHeader: string }[]>(() => {
    if (this.scope() === 'person') {
      const person = this.activePerson();
      if (!person) return [];
      const count = person.events.length;
      return [{
        title: `${person.name} — ${count} ${count === 1 ? 'event' : 'events'}`,
        teams: this.withEventLabels(person.events, true),
        teamColHeader: 'Event'
      }];
    }
    const sections: { title: string; teams: RegisteredTeamDto[]; teamColHeader: string }[] = [];
    const active = this.allPlayers().filter(p => p.active);
    const inactive = this.allPlayers().filter(p => !p.active);
    if (active.length) sections.push({ title: `Active (${active.length})`, teams: this.withEventLabels(active, false), teamColHeader: 'Player' });
    if (inactive.length) sections.push({ title: `Inactive (${inactive.length})`, teams: this.withEventLabels(inactive, false), teamColHeader: 'Player' });
    return sections;
  });

  familyName = computed(() => this.data()?.familyName ?? '');

  // Deposit/Balance columns only make sense when the fees actually carry a deposit.
  hasDeposit = computed(() => this.allPlayers().some(p => p.deposit > 0));

  // Family total owed — ALL registrations.
  familyOwedTotal = computed(() => this.allPlayers().reduce((s, p) => s + p.owedTotal, 0));

  // Rows feeding the ledger's top summary (Total Fees / Paid / Owed): the player's combined events
  // in person scope, else the whole family. The per-event amount caps in the add-record modal come
  // from addTargets (the picked event), not from this combined total.
  private summaryRows = computed<RegisteredTeamDto[]>(() =>
    this.scope() === 'person' ? (this.activePerson()?.events ?? []) : this.allPlayers());
  feeTotal = computed(() => this.summaryRows().reduce((s, p) => s + p.feeTotal, 0));
  paidTotal = computed(() => this.summaryRows().reduce((s, p) => s + p.paidTotal, 0));
  owedTotal = computed(() => this.summaryRows().reduce((s, p) => s + p.owedTotal, 0));
  checkOwed = computed(() => this.summaryRows().reduce((s, p) => s + p.ckOwedTotal, 0));

  // Per-registration ledger attribution (key = teamId = record.ownerRegistrationId).
  ledgerGroups = computed<LedgerGroup[]>(() => this.allPlayers().map(p => ({
    key: p.teamId,
    label: p.teamName,
    active: true
  })));

  // Records shown: the viewed player's combined set (ALL their events, regardless of team) in
  // person scope; else the whole family. Targeting an event for a charge does NOT filter this —
  // the combined ledger stays visible while the modal narrows to one event.
  accountingRecords = computed(() => {
    if (this.scope() === 'person') {
      const ids = new Set(this.activePerson()?.registrationIds ?? []);
      return this.allRecords().filter(r => r.ownerRegistrationId != null && ids.has(r.ownerRegistrationId));
    }
    return this.allRecords();
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
        if (!this.initialized) { this.initScope(); this.initialized = true; }
        else this.revalidateSelection();
        this.isLoading.set(false);
      },
      error: (err) => {
        this.loadError.set(err?.error?.message || 'Failed to load family accounting');
        this.isLoading.set(false);
      }
    });
  }

  /** Opening scope: a lone player opens to their (single) event ready to charge; everyone else
   *  opens the family overview. */
  private initScope(): void {
    const persons = this.persons();
    if (persons.length === 1) {
      this.selectPerson(persons[0].name);
    } else {
      this.scope.set('family');
      this.activePersonName.set(null);
      this.activeEventRegId.set(null);
    }
  }

  /** Keep the user's place across a post-payment reload; re-seed only if the selection vanished. */
  private revalidateSelection(): void {
    if (this.activePersonName() && !this.activePerson()) { this.initialized = false; this.initScope(); this.initialized = true; return; }
    if (this.activeEventRegId() && !this.allPlayers().some(p => p.teamId === this.activeEventRegId())) this.activeEventRegId.set(null);
  }

  setScope(s: Scope): void {
    this.scope.set(s);
    if (s === 'family') { this.activePersonName.set(null); this.activeEventRegId.set(null); }
  }

  /** View a player — their combined ledger across all events. The add-record modal picks which
   *  event a payment attaches to (auto-selected when there's only one). */
  selectPerson(name: string): void {
    this.activePersonName.set(name);
    this.activeEventRegId.set(null);
    this.scope.set('person');
  }

  /** The add-record picker chose a registration — point the payment/charge actions at it. */
  onAddTargetSelected(regId: string): void {
    this.activeEventRegId.set(regId);
  }

  // ── Accounting Ledger Handlers (target the selected event registration) ──

  onCcCharge(event: CcChargeEvent): void {
    const id = this.activeEventRegId();
    if (!id) return;
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
    const id = this.activeEventRegId();
    if (!id) return;
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
