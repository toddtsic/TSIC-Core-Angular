import { Injectable, inject, computed, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { formatHttpError } from '@views/registration/shared/utils/error-utils';
import { PlayerStateService } from '@views/registration/player/services/player-state.service';
import { TeamService } from '@views/registration/player/services/team.service';
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
    teamId: string;
    teamName: string;
    feeBase: number;
    feeProcessing: number;
    feeDiscount: number;
    feeLateFee: number;
    feeTotal: number;
    paidTotal: number;
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

    private readonly _discountMessage = signal<string | null>(null);
    private readonly _discountApplying = signal(false);
    private readonly _discountAppliedOk = signal(false);
    private readonly _selectedPaymentMethod = signal<'CC' | 'Check'>('CC');

    readonly discountMessage = this._discountMessage.asReadonly();
    readonly discountApplying = this._discountApplying.asReadonly();
    /** True after a discount was successfully applied (drives success styling on the message). */
    readonly discountAppliedOk = this._discountAppliedOk.asReadonly();
    readonly selectedPaymentMethod = this._selectedPaymentMethod.asReadonly();

    lineItems = computed<LineItem[]>(() => {
        const items: LineItem[] = [];
        const players = this.fp.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({ id: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const sel = selTeams[p.id];
            if (!sel) continue;
            // Normalize to array for CAC (multi-camp) support
            const teamIds = Array.isArray(sel) ? sel : [sel];
            for (const tid of teamIds) {
                if (typeof tid !== 'string' || !tid) continue;
                const team = this.teams.getTeamById(tid);
                const registration = this.getExistingRegistrationForTeam(p.id, tid);
                const financials = registration?.financials;
                if (!team && !registration) continue;
                const teamFee = this.getAmount(team);
                const feeBase = financials ? toNumber(financials.feeBase) : teamFee;
                const feeProcessing = financials ? toNumber(financials.feeProcessing) : 0;
                const feeDiscount = financials ? toNumber(financials.feeDiscount) : 0;
                const feeLateFee = financials ? toNumber(financials.feeLateFee) : 0;
                const feeTotal = financials ? toNumber(financials.feeTotal) : teamFee;
                const paidTotal = financials ? toNumber(financials.paidTotal) : 0;
                const amount = financials ? this.getAmountFromFinancials(financials) : teamFee;
                items.push({ playerId: p.id, playerName: p.name, teamId: tid, teamName: team?.teamName || registration?.assignedTeamName || '', feeBase, feeProcessing, feeDiscount, feeLateFee, feeTotal, paidTotal, amount });
            }
        }
        return items;
    });

    private readonly existingBalanceTotal = computed(() =>
        this.lineItems().filter(li => !!this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );
    private readonly newSelectionTotal = computed(() =>
        this.lineItems().filter(li => !this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );

    totalAmount = computed(() => this.existingBalanceTotal() + this.newSelectionTotal());

    depositTotal = computed(() => {
        let sum = 0;
        for (const li of this.lineItems()) {
            if (this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials) continue;
            const team = this.teams.getTeamById(li.teamId);
            sum += Number(team?.deposit ?? 0) || 0;
        }
        return sum;
    });

    isArbScenario = computed(() => !!this.jobCtx.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        const payable = this.lineItems().filter(li => !this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials);
        if (payable.length === 0) return false;
        return payable.every(li => {
            const team = this.teams.getTeamById(li.teamId);
            return (Number(team?.deposit) > 0 && Number(team?.fee) > 0);
        });
    });

    currentTotal = computed(() => {
        const opt = this.jobCtx.paymentOption();
        const existing = this.existingBalanceTotal();
        const base = opt === 'Deposit' ? existing + this.depositTotal() : this.totalAmount();
        return Math.max(0, base);
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

    // ── Payment method (CC vs Check) ────────────────────────────────────
    isCheckOnly = computed(() => this.jobCtx.paymentMethodsAllowedCode() === 3);
    showPaymentMethodSelector = computed(() => this.jobCtx.paymentMethodsAllowedCode() === 2);
    isCheckPayment = computed(() => this._selectedPaymentMethod() === 'Check');
    isCcPayment = computed(() => this._selectedPaymentMethod() === 'CC');
    payTo = computed(() => this.jobCtx.payTo());
    mailTo = computed(() => this.jobCtx.mailTo());
    mailinPaymentWarning = computed(() => this.jobCtx.mailinPaymentWarning());

    /** Total processing fees across all line items (from existing registrations). */
    totalProcessingFees = computed(() => {
        let sum = 0;
        for (const li of this.lineItems()) {
            const reg = this.getExistingRegistrationForTeam(li.playerId, li.teamId);
            if (reg?.financials) {
                sum += toNumber(reg.financials.feeProcessing);
            }
        }
        return sum;
    });

    /** Amount saved by paying with check instead of CC. */
    processingFeeSavings = computed(() =>
        this.jobCtx.bAddProcessingFees() ? this.totalProcessingFees() : 0
    );

    /** Check payment amount (total minus processing fees). */
    checkTotal = computed(() => Math.max(0, this.currentTotal() - this.processingFeeSavings()));

    selectPaymentMethod(method: 'CC' | 'Check'): void {
        this._selectedPaymentMethod.set(method);
    }

    /**
     * Initialize payment method based on job config.
     * Called after job metadata loads. Defaults to Check if check-only.
     */
    initPaymentMethod(): void {
        if (this.jobCtx.paymentMethodsAllowedCode() === 3) {
            this._selectedPaymentMethod.set('Check');
        } else {
            this._selectedPaymentMethod.set('CC');
        }
    }

    resetDiscount(): void {
        this._discountMessage.set(null);
        this._discountAppliedOk.set(false);
    }

    applyDiscount(code: string): void {
        if (!code || this._discountApplying()) return;
        const option = this.jobCtx.paymentOption();
        const items: ApplyDiscountItemDto[] = this.lineItems().map(li => ({
            playerId: li.playerId,
            amount: option === 'Deposit' ? this.getDepositForPlayer(li.playerId) : li.amount,
        }));
        if (items.length === 0) {
            this._discountMessage.set('No payable items eligible for discount');
            return;
        }
        this._discountApplying.set(true);
        this._discountMessage.set(null);
        this._discountAppliedOk.set(false);
        const req: ApplyDiscountRequestDto = { jobPath: this.jobCtx.jobPath(), code, items };
        this.http.post<ApplyDiscountResponseDto>(`${environment.apiUrl}/player-registration/apply-discount`, req)
            .subscribe({
                next: (resp: ApplyDiscountResponseDto) => {
                    const total = resp?.totalDiscount ?? 0;
                    if (resp?.success && toNumber(total) > 0) {
                        this._discountAppliedOk.set(true);
                        this._discountMessage.set(resp?.message || 'Discount applied');
                        // Reload family players — the server has already persisted the
                        // discount, so the reload brings back correct financials.
                        // Spinner stays on until the reload completes so the UI
                        // doesn't flash stale amounts between POST and reload.
                        const jobPath = this.jobCtx.jobPath();
                        const apiBase = this.jobCtx.resolveApiBase();
                        this.fp.loadFamilyPlayersOnce(jobPath, apiBase)
                            .catch(err => console.warn('[PaymentV2] refresh after discount failed', err))
                            .finally(() => this._discountApplying.set(false));
                    } else {
                        this._discountApplying.set(false);
                        this._discountAppliedOk.set(false);
                        this._discountMessage.set(resp?.message || 'Invalid or ineligible discount code');
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this._discountApplying.set(false);
                    this._discountAppliedOk.set(false);
                    this._discountMessage.set(formatHttpError(err));
                },
            });
    }

    private getExistingRegistrationForTeam(playerId: string, teamId: string) {
        const p = this.fp.familyPlayers().find(fp => fp.playerId === playerId);
        if (!p?.priorRegistrations?.length) return null;
        return p.priorRegistrations.find(r => r.assignedTeamId === teamId) ?? null;
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

    private getAmount(team: { fee?: number | string | null } | null | undefined): number {
        const v = Number(team?.fee ?? 0);
        return Number.isNaN(v) || v <= 0 ? 100 : v;
    }

    getDepositForPlayer(playerId: string): number {
        const teamId = this.playerState.selectedTeams()[playerId];
        const team = this.teams.getTeamById(teamId as string);
        return Number(team?.deposit ?? 0) || 0;
    }

    submitPayment(request: PaymentRequestDto): Observable<PaymentResponseDto> {
        return this.http.post<PaymentResponseDto>(`${environment.apiUrl}/player-registration/submit-payment`, request);
    }
}
