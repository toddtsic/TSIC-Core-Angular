import { Injectable, inject, computed, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { formatHttpError } from '@views/registration/wizards/shared/utils/error-utils';
import { PlayerStateService } from '@views/registration/wizards/player-registration-wizard/services/player-state.service';
import { TeamService } from '@views/registration/wizards/player-registration-wizard/team.service';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import type {
    ApplyDiscountItemDto,
    ApplyDiscountRequestDto,
    ApplyDiscountResponseDto,
    PaymentRequestDto,
    PaymentResponseDto,
    RegistrationFinancialsDto,
} from '@core/api';

function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

export interface LineItem {
    playerId: string;
    playerName: string;
    teamName: string;
    amount: number;
}

/**
 * Payment v2 — same business logic as PaymentService,
 * but reads from decomposed services instead of RegistrationWizardService.
 */
@Injectable({ providedIn: 'root' })
export class PaymentV2Service {
    private readonly jobCtx = inject(JobContextService);
    private readonly fp = inject(FamilyPlayersService);
    private readonly playerState = inject(PlayerStateService);
    private readonly teams = inject(TeamService);
    private readonly http = inject(HttpClient);

    private readonly _appliedDiscount = signal(0);
    private readonly _discountMessage = signal<string | null>(null);
    private readonly _discountApplying = signal(false);
    private readonly _appliedDiscountResponse = signal<ApplyDiscountResponseDto | null>(null);

    readonly appliedDiscount = this._appliedDiscount.asReadonly();
    readonly discountMessage = this._discountMessage.asReadonly();
    readonly discountApplying = this._discountApplying.asReadonly();
    readonly appliedDiscountResponse = this._appliedDiscountResponse.asReadonly();

    lineItems = computed<LineItem[]>(() => {
        const items: LineItem[] = [];
        const players = this.fp.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({ id: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const teamId = selTeams[p.id];
            if (!teamId || typeof teamId !== 'string') continue;
            const team = this.teams.getTeamById(teamId);
            const registration = this.getExistingRegistration(p.id);
            const financials = registration?.financials;
            if (!team && !registration) continue;
            const amount = financials ? this.getAmountFromFinancials(financials) : this.getAmount(team);
            items.push({ playerId: p.id, playerName: p.name, teamName: team?.teamName || registration?.assignedTeamName || '', amount });
        }
        return items;
    });

    private readonly existingBalanceTotal = computed(() =>
        this.lineItems().filter(li => !!this.getExistingRegistration(li.playerId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );
    private readonly newSelectionTotal = computed(() =>
        this.lineItems().filter(li => !this.getExistingRegistration(li.playerId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );

    totalAmount = computed(() => this.existingBalanceTotal() + this.newSelectionTotal());

    depositTotal = computed(() => {
        const selTeams = this.playerState.selectedTeams();
        let sum = 0;
        for (const li of this.lineItems()) {
            if (this.getExistingRegistration(li.playerId)?.financials) continue;
            const teamId = selTeams[li.playerId];
            const team = this.teams.getTeamById(teamId as string);
            sum += Number(team?.perRegistrantDeposit ?? 0) || 0;
        }
        return sum;
    });

    isArbScenario = computed(() => !!this.jobCtx.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        const payable = this.lineItems().filter(li => !this.getExistingRegistration(li.playerId)?.financials);
        if (payable.length === 0) return false;
        return payable.every(li => {
            const team = this.teams.getTeamById(this.playerState.selectedTeams()[li.playerId] as string);
            return (Number(team?.perRegistrantDeposit) > 0 && Number(team?.perRegistrantFee) > 0);
        });
    });

    currentTotal = computed(() => {
        const opt = this.jobCtx.paymentOption();
        const existing = this.existingBalanceTotal();
        const base = opt === 'Deposit' ? existing + this.depositTotal() : this.totalAmount();
        return Math.max(0, base - this._appliedDiscount());
    });

    arbOccurrences = computed(() => this.jobCtx.adnArbBillingOccurences() || 10);
    arbIntervalLength = computed(() => this.jobCtx.adnArbIntervalLength() || 1);
    arbStartDate = computed(() => {
        const raw = this.jobCtx.adnArbStartDate();
        return raw ? new Date(raw) : new Date(Date.now() + 24 * 60 * 60 * 1000);
    });
    arbPerOccurrence = computed(() => {
        const occ = this.arbOccurrences();
        const tot = this.totalAmount();
        return occ > 0 ? Math.round((tot / occ) * 100) / 100 : tot;
    });

    monthLabel(): string { return this.arbIntervalLength() === 1 ? 'month' : 'months'; }

    resetDiscount(): void {
        this._appliedDiscount.set(0);
        this._discountMessage.set(null);
    }

    applyDiscount(code: string): void {
        if (!code || this._discountApplying()) return;
        const option = this.jobCtx.paymentOption();
        const items: ApplyDiscountItemDto[] = this.lineItems().map(li => ({
            playerId: li.playerId,
            amount: option === 'Deposit' ? this.getDepositForPlayer(li.playerId) : li.amount,
        }));
        if (items.length === 0) {
            this._appliedDiscount.set(0);
            this._discountMessage.set('No payable items eligible for discount');
            return;
        }
        this._discountApplying.set(true);
        this._discountMessage.set(null);
        const req: ApplyDiscountRequestDto = { jobPath: this.jobCtx.jobPath(), code, items };
        this.http.post<ApplyDiscountResponseDto>(`${environment.apiUrl}/player-registration/apply-discount`, req)
            .subscribe({
                next: (resp: ApplyDiscountResponseDto) => {
                    this._discountApplying.set(false);
                    this._appliedDiscountResponse.set(resp);
                    const total = resp?.totalDiscount ?? 0;
                    if (resp?.success && toNumber(total) > 0) {
                        this._appliedDiscount.set(0);
                        this._discountMessage.set(resp?.message || 'Discount applied');
                        if (resp?.updatedFinancials) this.mergeUpdatedFinancials(resp.updatedFinancials);
                        const jobPath = this.jobCtx.jobPath();
                        const apiBase = this.jobCtx.resolveApiBase();
                        this.fp.loadFamilyPlayersOnce(jobPath, apiBase).catch(err => console.warn('[PaymentV2] refresh after discount failed', err));
                    } else {
                        this._appliedDiscount.set(0);
                        this._discountMessage.set(resp?.message || 'Invalid or ineligible discount code');
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this._discountApplying.set(false);
                    this._appliedDiscountResponse.set(null);
                    this._appliedDiscount.set(0);
                    this._discountMessage.set(formatHttpError(err));
                },
            });
    }

    private getExistingRegistration(playerId: string) {
        const p = this.fp.familyPlayers().find(fp => fp.playerId === playerId);
        if (!p?.priorRegistrations?.length) return null;
        const active = p.priorRegistrations.find(r => r.active);
        return active ?? p.priorRegistrations.at(-1) ?? null;
    }

    private mergeUpdatedFinancials(updatedFinancials: Record<string, RegistrationFinancialsDto>): void {
        const players = this.fp.familyPlayers();
        const updated = players.map(p => {
            const newFin = updatedFinancials[p.playerId];
            if (!newFin || !p.priorRegistrations?.length) return p;
            const activeIdx = p.priorRegistrations.findIndex(r => r.active);
            const targetIdx = activeIdx >= 0 ? activeIdx : p.priorRegistrations.length - 1;
            const clone = { ...p, priorRegistrations: [...p.priorRegistrations] };
            clone.priorRegistrations[targetIdx] = { ...clone.priorRegistrations[targetIdx], financials: newFin };
            return clone;
        });
        this.fp.updateFamilyPlayers(updated);
    }

    private getAmountFromFinancials(financials: RegistrationFinancialsDto): number {
        if (financials?.owedTotal !== undefined && financials?.owedTotal !== null) {
            const owed = toNumber(financials.owedTotal);
            if (owed >= 0) return owed;
        }
        const base = toNumber(financials?.feeBase) + toNumber(financials?.feeProcessing) + toNumber(financials?.feeLateFee) + toNumber(financials?.feeDonation);
        const discount = toNumber(financials?.feeDiscount);
        const paid = toNumber(financials?.paidTotal);
        return Math.max(0, base - discount - paid);
    }

    private getAmount(team: { perRegistrantFee?: number | string | null } | null | undefined): number {
        const v = Number(team?.perRegistrantFee ?? 0);
        return Number.isNaN(v) || v <= 0 ? 100 : v;
    }

    getDepositForPlayer(playerId: string): number {
        const teamId = this.playerState.selectedTeams()[playerId];
        const team = this.teams.getTeamById(teamId as string);
        return Number(team?.perRegistrantDeposit ?? 0) || 0;
    }

    submitPayment(request: PaymentRequestDto): Observable<PaymentResponseDto> {
        return this.http.post<PaymentResponseDto>(`${environment.apiUrl}/registration/submit-payment`, request);
    }
}
