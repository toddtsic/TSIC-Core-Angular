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

    /**
     * Both phase datasets, computed once per dependency change. The radio toggle
     * reduces to a selector pick — the per-row math runs once per change in
     * familyPlayers, selectedTeams, or registrations, not per option flip.
     */
    private readonly lineItemsByPhase = computed<{ deposit: LineItem[]; pif: LineItem[] }>(() => {
        const deposit: LineItem[] = [];
        const pif: LineItem[] = [];
        const players = this.fp.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({ id: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const sel = selTeams[p.id];
            if (!sel) continue;
            const teamIds = Array.isArray(sel) ? sel : [sel];
            for (const tid of teamIds) {
                if (typeof tid !== 'string' || !tid) continue;
                const team = this.teams.getTeamById(tid);
                const registration = this.getExistingRegistrationForTeam(p.id, tid);
                if (!team && !registration) continue;
                deposit.push(this.buildLineItem(p.id, p.name, tid, team, registration, false));
                pif.push(this.buildLineItem(p.id, p.name, tid, team, registration, true));
            }
        }
        return { deposit, pif };
    });

    lineItems = computed<LineItem[]>(() => {
        const both = this.lineItemsByPhase();
        return this.jobCtx.paymentOption() === 'PIF' ? both.pif : both.deposit;
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

    /**
     * What the parent would be charged if they picked Deposit — independent of
     * the currently selected option. Sums amounts from the precomputed deposit
     * array (no re-iteration of per-row math).
     */
    depositOptionTotal = computed(() =>
        this.lineItemsByPhase().deposit.reduce((sum, li) => sum + li.amount, 0),
    );

    /**
     * What the parent would be billed *after* paying the deposit — i.e. the
     * remaining balance the director will collect later. Equal to the
     * difference between the PIF and Deposit options so that
     *   depositOptionTotal + depositOptionRemainder ≈ pifOptionTotal
     * holds true under proportional processing.
     */
    depositOptionRemainder = computed(() => Math.max(0, this.pifOptionTotal() - this.depositOptionTotal()));

    /**
     * What the parent would be charged if they picked Pay In Full — independent
     * of the currently selected option. Sums amounts from the precomputed PIF
     * array (no re-iteration of per-row math).
     */
    pifOptionTotal = computed(() =>
        this.lineItemsByPhase().pif.reduce((sum, li) => sum + li.amount, 0),
    );

    isArbScenario = computed(() => !!this.jobCtx.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        if (this.jobCtx.bPlayersFullPaymentRequired()) return false;
        // Eligible = new (unsubmitted) lines OR existing deposit-phase lines (paid or unpaid)
        // where the parent could upgrade to PIF. Existing rows already at full-payment base
        // do not open the radio because there is no upgrade path from this checkout.
        const lines = this.lineItems();
        const eligible = lines.filter(li => {
            const reg = this.getExistingRegistrationForTeam(li.playerId, li.teamId);
            if (!reg?.financials) return true;
            return this.isExistingDepositPhase(li.teamId, reg.financials);
        });
        if (eligible.length === 0) return false;
        return eligible.every(li => {
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
                sum += li.feeProcessing;
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

    /**
     * Resolve the displayed FeeBase for an unsubmitted line item, mirroring the
     * backend's ApplyNewRegistrationFeesAsync logic exactly:
     *   - phaseExpectsFull → Deposit + BalanceDue (i.e. team.deposit + team.fee)
     *   - else             → Deposit when configured, else BalanceDue
     * The backend collapses team.deposit to 0 when Deposit==BalanceDue (single-phase
     * jobs), so the deposit-first branch produces team.fee for those — same as
     * EffectiveDeposit's NULL-coalesce in IFeeRepository.
     */

    /** Build one LineItem at the given phase. Used twice per (player × team) pair
     * by lineItemsByPhase to produce the deposit and PIF arrays in one pass. */
    private buildLineItem(
        playerId: string,
        playerName: string,
        teamId: string,
        team: { fee?: number | string | null; deposit?: number | string | null; teamName?: string | null } | null | undefined,
        registration: { assignedTeamName?: string | null; financials?: RegistrationFinancialsDto | null } | null | undefined,
        phaseExpectsFull: boolean,
    ): LineItem {
        const financials = registration?.financials;
        const phasedTeamFee = this.getPhasedTeamFee(team, phaseExpectsFull);
        const upgrade = this.computePifUpgrade(team, financials, phaseExpectsFull);
        const feeBase = upgrade ? upgrade.feeBase : (financials ? toNumber(financials.feeBase) : phasedTeamFee);
        const feeProcessing = upgrade ? upgrade.feeProcessing : (financials ? toNumber(financials.feeProcessing) : 0);
        const feeDiscount = financials ? toNumber(financials.feeDiscount) : 0;
        const feeLateFee = financials ? toNumber(financials.feeLateFee) : 0;
        const feeTotal = upgrade ? upgrade.feeTotal : (financials ? toNumber(financials.feeTotal) : phasedTeamFee);
        const paidTotal = financials ? toNumber(financials.paidTotal) : 0;
        const amount = upgrade ? upgrade.amount : (financials ? this.getAmountFromFinancials(financials) : phasedTeamFee);
        return {
            playerId,
            playerName,
            teamId,
            teamName: team?.teamName || registration?.assignedTeamName || '',
            feeBase,
            feeProcessing,
            feeDiscount,
            feeLateFee,
            feeTotal,
            paidTotal,
            amount,
        };
    }

    /**
     * True when an existing registration is still in deposit phase — eligible
     * for voluntary PIF upgrade at checkout. Includes both unpaid AND paid deposit
     * rows: paid rows true-up to the balance under PIF, unpaid rows pay full.
     * Test: team has deposit and fee configured AND stamped feeBase is below
     * the full (deposit+fee) total.
     */
    private isExistingDepositPhase(
        teamId: string,
        financials: RegistrationFinancialsDto,
    ): boolean {
        const team = this.teams.getTeamById(teamId);
        const deposit = Number(team?.deposit ?? 0) || 0;
        const fee = Number(team?.fee ?? 0) || 0;
        if (deposit <= 0 || fee <= 0) return false;
        return toNumber(financials.feeBase) < (deposit + fee);
    }

    /**
     * If the parent voluntarily picks PIF on an existing deposit-phase line, mirror
     * ApplyPifUpgradeAsync's stamping for display. Applies to ALL prior deposit-phase
     * rows (paid and unpaid):
     *   - feeBase  = deposit + fee
     *   - feeProcessing scaled proportionally from the original rate
     *   - feeTotal/amount reflect the upgraded charge minus discount minus paid
     * Paid-deposit rows produce the balance true-up; unpaid rows produce the full
     * upgrade amount. Returns null when no upgrade applies (already at full base,
     * deposit/fee not configured, or wrong phase).
     */
    private computePifUpgrade(
        team: { fee?: number | string | null; deposit?: number | string | null } | null | undefined,
        financials: RegistrationFinancialsDto | null | undefined,
        phaseExpectsFull: boolean,
    ): { feeBase: number; feeProcessing: number; feeTotal: number; amount: number } | null {
        if (!financials || !phaseExpectsFull) return null;
        const deposit = Number(team?.deposit ?? 0) || 0;
        const fee = Number(team?.fee ?? 0) || 0;
        if (deposit <= 0 || fee <= 0) return null;
        const origBase = toNumber(financials.feeBase);
        const newBase = deposit + fee;
        if (newBase <= origBase) return null;
        const procRatio = origBase > 0 ? newBase / origBase : 1;
        const origProc = toNumber(financials.feeProcessing);
        const newProc = origProc * procRatio;
        const lateFee = toNumber(financials.feeLateFee);
        const discount = toNumber(financials.feeDiscount);
        const paid = toNumber(financials.paidTotal);
        const total = newBase + newProc + lateFee - discount;
        return {
            feeBase: newBase,
            feeProcessing: newProc,
            feeTotal: total,
            amount: Math.max(0, total - paid),
        };
    }

    private getPhasedTeamFee(
        team: { fee?: number | string | null; deposit?: number | string | null } | null | undefined,
        phaseExpectsFull: boolean,
    ): number {
        const fee = Number(team?.fee ?? 0) || 0;
        const deposit = Number(team?.deposit ?? 0) || 0;
        if (phaseExpectsFull) {
            const total = deposit + fee;
            return total > 0 ? total : this.getAmount(team);
        }
        return deposit > 0 ? deposit : this.getAmount(team);
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
