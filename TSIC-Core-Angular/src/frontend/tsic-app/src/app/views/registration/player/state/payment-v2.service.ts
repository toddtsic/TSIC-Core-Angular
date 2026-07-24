import { Injectable, inject, computed, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
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
    SubmitByCheckRequestDto,
    SubmitByCheckResponseDto,
} from '@core/api';

function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

/** Half-up to cents — mirrors the server's Math.Round(x, 2, MidpointRounding.AwayFromZero) on the
 *  non-negative amounts this file deals in. The 1e-7 nudge counters binary-double representation
 *  error: a true half-cent like 38.065 is stored as 38.064999…, which bare Math.round sends DOWN
 *  while the server's decimal math rounds UP. Real money values sit ≥ 1e-4 (of a cent) from any
 *  rounding boundary, so the nudge can never flip a non-midpoint value. */
function roundCents(value: number): number {
    return Math.round(value * 100 + 1e-7) / 100;
}

/** Floor to cents — mirrors the server's ARB per-occurrence math (PaymentService
 *  .CreateArbSubscriptionsCoreAsync floors so a plan can never draft more than the owed
 *  basis; the cent remainder is forgiven server-side as a Correction credit at mint).
 *  The 1e-7 nudge keeps exact-cent quotients from flooring a cent low on FP dust
 *  (e.g. 79.98/6 → 1332.9999… must floor to 1333, not 1332). */
function floorCents(value: number): number {
    return Math.floor(value * 100 + 1e-7) / 100;
}

/** Authorize.Net subscription statuses that can no longer draft the card. Mirrors
 *  PaymentService.DeadArbStatuses — "suspended" is NOT here: the gateway resumes a suspended
 *  subscription once the card clears, so it still bills. */
const DEAD_ARB_STATUSES = ['canceled', 'terminated', 'expired'];

/** True when this registration is already financed by a subscription that can still bill the card.
 *  A canceled subscription leaves its id behind (admin cancel), so re-enrollment stays possible. */
function hasLiveArbSubscription(
    registration: { adnSubscriptionId?: string | null; adnSubscriptionStatus?: string | null } | null | undefined,
): boolean {
    if (!registration?.adnSubscriptionId) return false;
    return !DEAD_ARB_STATUSES.includes((registration.adnSubscriptionStatus || '').toLowerCase());
}

/** One player's recurring-billing plan — the client mirror of the single Authorize.Net subscription
 *  the server mints for that player's registration. Carries the accounting columns alongside the
 *  plan so the ARB table renders from one row object. */
export interface ArbPlanLine {
    playerId: string;
    playerName: string;
    teamName: string;
    feeBase: number;
    feeAdj: number;
    feeTotal: number;
    /** This player's own owed balance — the subscription's financing basis. */
    owed: number;
    /** Charged per cycle for THIS player. Zero when the line owes nothing (no subscription minted). */
    perOccurrence: number;
    /** This player is already carried by a live subscription — no new plan is minted for them. */
    alreadyEnrolled: boolean;
    /** The cycle amount of the subscription ALREADY billing this player. Zero unless alreadyEnrolled. */
    enrolledPerOccurrence: number;
    /** The occurrence count of the subscription ALREADY billing this player. Zero unless alreadyEnrolled. */
    enrolledOccurrences: number;
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
    /** Canonical signed Fee-Adj (PaymentState): lateFee − discount − correction. Late fee /
     *  correction-charge positive; discount / correction-credit negative. Folds in the old
     *  Discount column. */
    feeAdj: number;
    feeTotal: number;
    paidTotal: number;
    /** Real money received (PaymentState.TenderPaid) — excludes corrections, which surface in
     *  feeAdj. The "Paid" column binds this, not paidTotal. */
    tenderPaid: number;
    amount: number;
    /** This line's charge if paid by eCheck (CC charge minus the eCheck proc-rate credit).
     *  Existing regs carry the server's per-reg figure (EcheckOwedTotal); a client PIF upgrade
     *  carries its own method-correct quote (computePifUpgrade mirrors the server's at-charge
     *  realize). Equals `amount` when there's no credit (proc off, new line). Display + the
     *  eCheck expectedTotal — the backend recomputes and refuses on drift. */
    echeckAmount: number;
    /** This line's charge if paid by mailed check (CC charge minus the FULL CC proc). Equals
     *  `amount` when there's no proc (proc off, or a new line that carries no proc yet). The
     *  server-computed per-reg CheckOwedTotal — NOT a client baseTotal − Σ(stamped proc), which
     *  wrongly subtracts a paid sibling's stamped proc from a new player's amount; a client PIF
     *  upgrade carries its own method-correct quote. Display only. */
    checkAmount: number;
    /** False when this line's team has no fee configured at any cascade level — the wizard
     *  blocks completion instead of charging/fabricating. Always true for existing
     *  registrations (already stamped with a real fee). */
    feeConfigured: boolean;
    /** This registration's balance is already financed by a live ARB subscription. ARB records no
     *  money — paidTotal stays 0 and owedTotal stays full for the life of the plan — so `amount`
     *  looks exactly like an unpaid line. It must never be charged again. The server enforces the
     *  same rule (PaymentService.PartitionArbEnrolled); this flag keeps the screen in step with it. */
    arbEnrolled: boolean;
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
    private readonly _selectedPaymentMethod = signal<'CC' | 'Echeck' | 'Check'>('CC');

    readonly discountMessage = this._discountMessage.asReadonly();
    readonly discountApplying = this._discountApplying.asReadonly();
    /** True after a discount was successfully applied (drives success styling on the message). */
    readonly discountAppliedOk = this._discountAppliedOk.asReadonly();
    readonly selectedPaymentMethod = this._selectedPaymentMethod.asReadonly();

    /** Optional donor-entered gift (principal). Repriced client-side off the server-supplied
     *  effective rate; the server is authoritative on submit (it re-levies the same proc). */
    private readonly _donation = signal(0);
    readonly donation = this._donation.asReadonly();
    setDonation(v: number | string): void {
        const n = typeof v === 'string' ? Number.parseFloat(v) : v;
        this._donation.set(Number.isFinite(n) && n > 0 ? Math.round(n * 100) / 100 : 0);
    }
    resetDonation(): void { this._donation.set(0); }

    /**
     * Per (player × team) pair: BOTH phase builds plus the pair's OWN resolved phase.
     * Computed once per dependency change (familyPlayers, selectedTeams, registrations) —
     * the radio toggle only re-selects, it does not re-run the per-row math.
     *
     * isFullPhase is per-row (NOT a cart-wide flag): a family cart can span scopes that
     * differ in phase (per-scope JobFees.BFullPaymentRequired cascade). A full-payment line
     * is ALWAYS billed in full; a deposit-eligible line follows the Deposit/PIF radio. This
     * mirrors the server's per-registration charge (PaymentService.ComputeChargesAsync).
     */
    private readonly linePairs = computed<{ deposit: LineItem; pif: LineItem; isFullPhase: boolean }[]>(() => {
        const pairs: { deposit: LineItem; pif: LineItem; isFullPhase: boolean }[] = [];
        const players = this.fp.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({ id: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const teamIds = selTeams[p.id] ?? [];
            for (const tid of teamIds) {
                if (typeof tid !== 'string' || !tid) continue;
                const team = this.teams.getTeamById(tid);
                const registration = this.getExistingRegistrationForTeam(p.id, tid);
                if (!team && !registration) continue;
                pairs.push({
                    deposit: this.buildLineItem(p.id, p.name, tid, team, registration, false),
                    pif: this.buildLineItem(p.id, p.name, tid, team, registration, true),
                    isFullPhase: this.isLineFullPhase(tid, registration, team),
                });
            }
        }
        return pairs;
    });

    lineItems = computed<LineItem[]>(() => {
        const pif = this.jobCtx.paymentOption() === 'PIF';
        // Full-payment lines always bill in full; deposit-eligible lines follow the radio.
        return this.linePairs().map(p => (p.isFullPhase || pif) ? p.pif : p.deposit);
    });

    /**
     * The lines this submission can actually charge. A registration already carried by a live ARB
     * subscription is financed, not owed today, and the server drops it from every charge path
     * (PaymentService.PartitionArbEnrolled) — so it must not reach any total the parent is quoted
     * or that we send as `expectedTotal`, or the shown↔charged guard (AMOUNT_MISMATCH) trips.
     *
     * It stays in lineItems() so the accounting table can still show the player and their plan.
     */
    private readonly billablePairs = computed(() => this.linePairs().filter(p => !p.pif.arbEnrolled));
    private readonly billableLineItems = computed(() => this.lineItems().filter(li => !li.arbEnrolled));

    private readonly existingBalanceTotal = computed(() =>
        this.billableLineItems().filter(li => !!this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );
    private readonly newSelectionTotal = computed(() =>
        this.billableLineItems().filter(li => !this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials).reduce((sum, li) => sum + li.amount, 0),
    );

    // lineItems() now carries each line's resolved phase, so this per-line sum is the correct
    // charge total for EITHER option (deposit-eligible lines reflect the radio; full lines stay
    // full). baseTotal therefore derives straight from it — no separate deposit/PIF branch.
    totalAmount = computed(() => this.existingBalanceTotal() + this.newSelectionTotal());

    /** Deposit charged TODAY for the NEW selections only (existing balances surface separately).
     *  Phase-aware: a new full-payment line owes its FULL price today, a deposit line its deposit. */
    depositTotal = computed(() => {
        let sum = 0;
        for (const li of this.lineItems()) {
            if (this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.financials) continue;
            const team = this.teams.getTeamById(li.teamId);
            const deposit = Number(team?.deposit ?? 0) || 0;
            const fee = Number(team?.fee ?? 0) || 0;
            sum += team?.fullPaymentRequired ? (deposit + fee) : deposit;
        }
        return sum;
    });

    /** This line's charge under the currently-selected method. echeckAmount / checkAmount equal
     *  `amount` when no proc credit applies (proc off, new line), so this collapses to the CC
     *  amount in those cases — the same picker the accounting table uses (owesFor). Keeps the
     *  Deposit/PIF radio labels method-reactive so they track the table and the Pay button. */
    private methodAmount(li: LineItem): number {
        return this.isEcheckPayment() ? li.echeckAmount
             : this.isCheckPayment()  ? li.checkAmount
             : li.amount;
    }

    /**
     * What the parent would be charged if they picked Deposit — independent of the currently
     * selected option, but reactive to the selected METHOD. Per-row: full-payment lines contribute
     * their full charge, deposit-eligible lines their deposit. (Display only — drives the radio label.)
     */
    depositOptionTotal = computed(() =>
        this.billablePairs().reduce((sum, p) => sum + this.methodAmount(p.isFullPhase ? p.pif : p.deposit), 0),
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
     * What the parent would be charged if they picked Pay In Full — independent of the currently
     * selected option, but reactive to the selected METHOD. Every line at its full charge, at the
     * method's rate (eCheck/check drop the CC proc spread — including on a PIF upgrade of a
     * deposit-phase line, whose quote computePifUpgrade derives method-correct).
     */
    pifOptionTotal = computed(() =>
        this.billablePairs().reduce((sum, p) => sum + this.methodAmount(p.pif), 0),
    );

    isArbScenario = computed(() => !!this.jobCtx.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        // Open the Deposit/PIF radio when ANY line is in deposit phase — NOT all (mirrors the
        // server's ANY-deposit rule, PaymentService.IsDepositScenarioAsync). A mixed cart still
        // offers the deposit option for its deposit-eligible players; full-payment lines are
        // billed in full regardless of the radio. Per-row isFullPhase already folds in the job
        // baseline (bPlayersFullPaymentRequired) via the team's server-resolved phase, so no
        // separate cart-wide gate is needed.
        return this.linePairs().some(p => {
            if (p.isFullPhase) return false;
            const team = this.teams.getTeamById(p.deposit.teamId);
            return (Number(team?.deposit) > 0 && Number(team?.fee) > 0);
        });
    });

    /** Charge total BEFORE any optional donation — the pre-donation base every method total
     *  derives from. Equals currentTotal when no donation is entered (donation 0 ⇒ no number moves).
     *  lineItems() already carries each line's resolved phase (deposit-eligible lines follow the
     *  radio; full lines stay full), so totalAmount IS the right base for either option — no
     *  separate Deposit/PIF branch. This keeps client ExpectedTotal == the server per-reg charge. */
    readonly baseTotal = computed(() => Math.max(0, this.totalAmount()));

    /** Donation's CC-path contribution: principal + CC processing (when the job adds proc). */
    readonly donationCc = computed(() => {
        const d = this.donation();
        if (d <= 0) return 0;
        return d + (this.jobCtx.bAddProcessingFees() ? d * this.jobCtx.effectiveProcessingRate() : 0);
    });
    /** Donation's eCheck-path contribution: principal + eCheck processing (the lower ACH rate). */
    readonly donationEcheck = computed(() => {
        const d = this.donation();
        if (d <= 0) return 0;
        return d + (this.jobCtx.bAddProcessingFees() ? d * this.jobCtx.effectiveEcheckProcessingRate() : 0);
    });
    /** Just the CC processing levied on the donation — itemized as its own accounting line. */
    readonly donationProcessing = computed(() => Math.max(0, this.donationCc() - this.donation()));

    /** CC charge total INCLUDING any donation — drives the accounting Total Due and the CC button. */
    currentTotal = computed(() => this.baseTotal() + this.donationCc());

    arbOccurrences = computed(() => this.jobCtx.adnArbBillingOccurences() || 10);
    arbIntervalLength = computed(() => this.jobCtx.adnArbIntervalLength() || 1);
    arbStartDate = computed(() => {
        const raw = this.jobCtx.adnArbStartDate();
        return raw ? new Date(raw) : new Date(Date.now() + 24 * 60 * 60 * 1000);
    });
    /**
     * Per-player recurring-billing plan. The server creates ONE Authorize.Net subscription per
     * REGISTRATION (PaymentService.CreateArbSubscriptionsAsync), financing that player's own owed
     * balance — `li.amount`, the client mirror of `reg.OwedTotal` — over the job's shared occurrence
     * count. Basis is the OWED amount, not feeTotal: a line with prior payments finances only what
     * remains, exactly as the server does.
     *
     * A line owing nothing mints no subscription (the server activates it directly, since the
     * gateway rejects a $0 recurring charge), so it carries perOccurrence = 0.
     *
     * A line ALREADY carried by a live subscription also mints nothing — the server excludes it from
     * the charge set. It keeps its row (the parent should see the player is covered) but reports the
     * plan already billing them, not a fresh quote that would be a second subscription.
     */
    readonly arbPlanLines = computed<ArbPlanLine[]>(() => {
        const occ = this.arbOccurrences();
        return this.lineItems().map(li => {
            const reg = this.getExistingRegistrationForTeam(li.playerId, li.teamId);
            const enrolled = li.arbEnrolled;
            return {
                playerId: li.playerId,
                playerName: li.playerName,
                teamName: li.teamName,
                feeBase: li.feeBase,
                feeAdj: li.feeAdj,
                feeTotal: li.feeTotal,
                owed: li.amount,
                perOccurrence: !enrolled && li.amount > 0 && occ > 0 ? floorCents(li.amount / occ) : 0,
                alreadyEnrolled: enrolled,
                enrolledPerOccurrence: enrolled ? toNumber(reg?.adnSubscriptionAmountPerOccurence) : 0,
                enrolledOccurrences: enrolled ? toNumber(reg?.adnSubscriptionBillingOccurences) : 0,
            };
        });
    });

    /** Only the lines that will actually mint a subscription on THIS submission. */
    readonly arbBilledPlanLines = computed(() => this.arbPlanLines().filter(l => !l.alreadyEnrolled && l.perOccurrence > 0));

    /**
     * What the card is actually charged each cycle: the SUM of the independently-floored per-player
     * installments. This is deliberately not round(familyTotal / occurrences) — the server floors
     * each subscription on its own, so summing the floored parts is the only figure that matches the
     * gateway. (e.g. two $100 players over 3 payments bill 33.33 + 33.33 = $66.66 per cycle, not the
     * $66.67 a rounded family total would promise.)
     */
    readonly arbInstallmentTotal = computed(() =>
        roundCents(this.arbBilledPlanLines().reduce((sum, l) => sum + l.perOccurrence, 0)));

    monthLabel(): string { return this.arbIntervalLength() === 1 ? 'month' : 'months'; }

    // ── Payment method (CC vs eCheck vs Check) ───────────────────────────
    isCheckOnly = computed(() => this.jobCtx.paymentMethodsAllowedCode() === 3);
    isCheckPayment = computed(() => this._selectedPaymentMethod() === 'Check');
    isCcPayment = computed(() => this._selectedPaymentMethod() === 'CC');
    isEcheckPayment = computed(() => this._selectedPaymentMethod() === 'Echeck');
    // Method visibility:
    //   • CC button: shown unless the job is check-only (code 3).
    //   • eCheck button: per-job opt-in.
    //   • Mail-in Check button: hidden when eCheck is enabled — eCheck is the
    //     online replacement for paper check, so admins shouldn't offer both.
    showCcButton = computed(() => this.jobCtx.paymentMethodsAllowedCode() !== 3);
    showEcheckButton = computed(() => this.jobCtx.bEnableEcheck());
    showCheckButton = computed(() =>
        this.jobCtx.paymentMethodsAllowedCode() !== 1 && !this.jobCtx.bEnableEcheck()
    );
    showPaymentMethodSelector = computed(() =>
        (this.showCcButton() ? 1 : 0) + (this.showEcheckButton() ? 1 : 0) + (this.showCheckButton() ? 1 : 0) >= 2
    );
    payTo = computed(() => this.jobCtx.payTo());
    mailTo = computed(() => this.jobCtx.mailTo());
    mailinPaymentWarning = computed(() => this.jobCtx.mailinPaymentWarning());

    /**
     * Amount saved by paying with check instead of CC — the FULL CC proc dropped, summed per
     * registration from the server-computed check owed (CheckOwedTotal). Mirrors echeckSavings
     * exactly, one rate apart.
     *
     * Per line: li.checkAmount is the server's per-reg check owed (== amount when proc is off or
     * the line carries no proc), so (amount − checkAmount) is THIS line's proc credit and nothing
     * more. A paid sibling contributes amount 0 / checkAmount 0 → 0 savings, so its stamped proc
     * is NOT subtracted from a new player's check amount (the old baseTotal − Σ(stamped proc)
     * derivation did exactly that — Ann's "base less proc" bug when adding to an existing reg).
     */
    checkSavings = computed(() => {
        if (!this.jobCtx.bAddProcessingFees()) return 0;
        // Billable lines only — checkTotal is baseTotal − checkSavings, and baseTotal already
        // excludes ARB-enrolled lines. Crediting their proc here would undershoot the check amount.
        return this.billableLineItems().reduce((sum, li) => sum + Math.max(0, li.amount - li.checkAmount), 0);
    });

    /** Check payment amount (base minus the full CC proc credit). Donation is excluded — a mailed
     *  check is not an online charge the system can levy a gift on, so it is never offered with check. */
    checkTotal = computed(() => Math.max(0, this.baseTotal() - this.checkSavings()));

    /**
     * Amount saved by paying with eCheck instead of CC — the (CC − eCheck) proc-rate credit,
     * summed per registration from the server-computed eCheck owed. Only the full-payment (PIF)
     * path carries a credit; a deposit charge sits at/below principal and is debited the same
     * by either method, so savings are zero there.
     */
    echeckSavings = computed(() => {
        if (!this.jobCtx.bAddProcessingFees()) return 0;
        // Per line: a full-payment line carries the (CC − eCheck) proc-rate credit in EITHER
        // option; a deposit charge sits at/below principal and is debited the same by either
        // method (echeckAmount == amount → contributes 0). So this per-line sum is correct even
        // under Deposit, where a mixed cart can contain full-payment lines that DO carry a credit.
        // Billable lines only, for the same reason checkSavings excludes ARB-enrolled lines.
        return this.billableLineItems().reduce((sum, li) => sum + Math.max(0, li.amount - li.echeckAmount), 0);
    });

    /** eCheck payment amount: the base minus the eCheck proc-rate savings, plus the donation
     *  charged at the (lower) eCheck rate. */
    echeckTotal = computed(() => Math.max(0, this.baseTotal() - this.echeckSavings()) + this.donationEcheck());

    selectPaymentMethod(method: 'CC' | 'Echeck' | 'Check'): void {
        this._selectedPaymentMethod.set(method);
    }

    /**
     * Initialize payment method based on job config.
     * Called after job metadata loads. Defaults to the first visible button in
     * priority order CC > Echeck > Check, so we never land on a hidden method
     * (e.g. check-only + eCheck enabled defaults to Echeck, not Check).
     */
    initPaymentMethod(): void {
        if (this.showCcButton()) {
            this._selectedPaymentMethod.set('CC');
        } else if (this.showEcheckButton()) {
            this._selectedPaymentMethod.set('Echeck');
        } else {
            this._selectedPaymentMethod.set('Check');
        }
    }

    resetDiscount(): void {
        this._discountMessage.set(null);
        this._discountAppliedOk.set(false);
    }

    /** Apply a discount code to the current line items. Resolves with the server response on a
     *  successful apply (after the financials reload completes), or null on a no-op/failure — the
     *  caller awaits this to remount the VerticalInsure widget against the refreshed offer. */
    applyDiscount(code: string): Promise<ApplyDiscountResponseDto | null> {
        if (!code || this._discountApplying()) return Promise.resolve(null);
        // Each item's amount must equal what that line actually contributes to currentTotal,
        // otherwise the gate (currentTotal > 0) can show the input while every item is 0 and the
        // backend rejects with "No valid players for discount". lineItems() already resolves each
        // line's phase (existing owed; new full → full; new deposit → deposit), so li.amount IS
        // that contribution — no per-option special-casing needed.
        //
        // ARB-enrolled lines are excluded: their subscription's per-cycle amount was fixed when it
        // was minted, so discounting the balance behind it would drop OwedTotal while the gateway
        // keeps drafting the original installment.
        const items: ApplyDiscountItemDto[] = this.billableLineItems().map(li =>
            // teamId identifies which camp this line is — a player with multiple camps has one
            // reg row per camp, so the backend needs it to discount every camp, not just the first.
            ({ playerId: li.playerId, teamId: li.teamId, amount: li.amount }));
        if (items.length === 0) {
            this._discountMessage.set('No payable items eligible for discount');
            return Promise.resolve(null);
        }
        this._discountApplying.set(true);
        this._discountMessage.set(null);
        this._discountAppliedOk.set(false);
        const req: ApplyDiscountRequestDto = { jobPath: this.jobCtx.jobPath(), code, items };
        return firstValueFrom(
            this.http.post<ApplyDiscountResponseDto>(`${environment.apiUrl}/player-registration/apply-discount`, req),
        )
            .then(async (resp: ApplyDiscountResponseDto) => {
                const total = resp?.totalDiscount ?? 0;
                if (resp?.success && toNumber(total) > 0) {
                    this._discountAppliedOk.set(true);
                    this._discountMessage.set(resp?.message || 'Discount applied');
                    // Push the rebuilt VI offer (server recomputed the insurable amount off the
                    // now-stamped discount) so the payment step can remount the widget with the
                    // corrected premium. A full waiver yields available=false → data:null → the
                    // offer region hides itself.
                    const offer = resp?.insuranceOffer;
                    if (offer) {
                        this.jobCtx.setVerticalInsureOffer({
                            loading: false,
                            data: offer.available ? (offer.playerObject ?? null) : null,
                            error: offer.error ?? null,
                        });
                    }
                    // Reload family players — the server has already persisted the discount, so the
                    // reload brings back correct financials. Spinner stays on until the reload
                    // completes so the UI doesn't flash stale amounts between POST and reload.
                    try {
                        await this.fp.loadFamilyPlayersOnce(this.jobCtx.jobPath(), this.jobCtx.resolveApiBase());
                    } catch (err) {
                        console.warn('[PaymentV2] refresh after discount failed', err);
                    } finally {
                        this._discountApplying.set(false);
                    }
                    return resp;
                }
                this._discountApplying.set(false);
                this._discountAppliedOk.set(false);
                this._discountMessage.set(resp?.message || 'Invalid or ineligible discount code');
                return resp ?? null;
            })
            .catch((err: HttpErrorResponse) => {
                this._discountApplying.set(false);
                this._discountAppliedOk.set(false);
                this._discountMessage.set(formatHttpError(err));
                return null;
            });
    }

    /**
     * Pay-by-check intake. Collects the family's existing registration IDs for the
     * current line items and asks the backend to stamp PaymentMethodChosen=3 +
     * BActive=true on each — holds the roster spot while the check is in transit.
     * Caller awaits this before advancing past the payment step.
     */
    submitByCheck(): Observable<SubmitByCheckResponseDto> {
        // ARB-enrolled registrations are already financed and active — re-stamping them as
        // paid-by-check would overwrite the payment method behind a running subscription.
        const registrationIds = this.billableLineItems()
            .map(li => this.getExistingRegistrationForTeam(li.playerId, li.teamId)?.registrationId)
            .filter((id): id is string => !!id);
        const req: SubmitByCheckRequestDto = {
            jobPath: this.jobCtx.jobPath(),
            registrationIds,
        };
        return this.http.post<SubmitByCheckResponseDto>(
            `${environment.apiUrl}/player-registration/submit-by-check`, req);
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

    // Never fabricate a price. A team with no resolvable fee returns 0 here; the line is
    // flagged feeConfigured=false (see buildLineItem) so the wizard blocks completion rather
    // than charging an invented amount. The old hardcoded $100 fallback masked missing fees.
    private getAmount(team: { fee?: number | string | null } | null | undefined): number {
        const v = Number(team?.fee ?? 0);
        return Number.isNaN(v) || v < 0 ? 0 : v;
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
     * by linePairs to produce the deposit and PIF builds in one pass. */
    private buildLineItem(
        playerId: string,
        playerName: string,
        teamId: string,
        team: { fee?: number | string | null; deposit?: number | string | null; teamName?: string | null; feeConfigured?: boolean | null } | null | undefined,
        registration: {
            assignedTeamName?: string | null;
            financials?: RegistrationFinancialsDto | null;
            adnSubscriptionId?: string | null;
            adnSubscriptionStatus?: string | null;
        } | null | undefined,
        phaseExpectsFull: boolean,
    ): LineItem {
        const financials = registration?.financials;
        const phasedTeamFee = this.getPhasedTeamFee(team, phaseExpectsFull);
        const upgrade = this.computePifUpgrade(team, financials, phaseExpectsFull);
        const feeBase = upgrade ? upgrade.feeBase : (financials ? toNumber(financials.feeBase) : phasedTeamFee);
        const feeProcessing = upgrade ? upgrade.feeProcessing : (financials ? toNumber(financials.feeProcessing) : 0);
        const feeDiscount = financials ? toNumber(financials.feeDiscount) : 0;
        const feeLateFee = financials ? toNumber(financials.feeLateFee) : 0;
        // Canonical signed Fee-Adj from the server (lateFee − discount − correction). An upgrade
        // re-derives base/proc but never touches lateFee/discount/corrections, so the server value
        // still holds; new regs (no financials) have no adjustment.
        const feeAdj = financials ? toNumber(financials.feeAdj) : 0;
        const feeTotal = upgrade ? upgrade.feeTotal : (financials ? toNumber(financials.feeTotal) : phasedTeamFee);
        const paidTotal = financials ? toNumber(financials.paidTotal) : 0;
        // Real money received (excludes corrections) — the "Paid" column. New regs have paid nothing.
        const tenderPaid = financials ? toNumber(financials.tenderPaid) : 0;
        const amount = upgrade ? upgrade.amount : (financials ? this.getAmountFromFinancials(financials) : phasedTeamFee);
        // eCheck / check charges: the server's per-reg method owed for an existing registration.
        // A client-side PIF upgrade re-derives fees the server figures predate, so it carries its
        // own method-correct quotes (computePifUpgrade mirrors the server's at-charge realize +
        // ResolveOwed). New regs have no financials → `amount` (no proc stamped yet to drop).
        const echeckAmount = upgrade ? upgrade.echeckAmount : (financials ? toNumber(financials.echeckOwedTotal) : amount);
        const checkAmount = upgrade ? upgrade.checkAmount : (financials ? toNumber(financials.checkOwedTotal) : amount);
        return {
            playerId,
            playerName,
            teamId,
            // Real team name from the loaded list; existing registrations fall back to the
            // server's assignedTeamName (already the twin's "WAITLIST - {name}" when the prior
            // placement was an actual waitlist). No synthetic prefix from rosterIsFull.
            teamName: team ? this.teams.getTeamDisplayName(teamId) : (registration?.assignedTeamName || ''),
            feeBase,
            feeProcessing,
            feeDiscount,
            feeLateFee,
            feeAdj,
            feeTotal,
            paidTotal,
            tenderPaid,
            amount,
            echeckAmount,
            checkAmount,
            // The team's current cascade signal. New orphan lines (and a pre-existing orphan
            // registration whose team is still fee-less) flag false; a missing/unknown team
            // defaults to true so only an explicit false blocks completion. A legitimately-free
            // configured event is feeConfigured=true with fee 0 — it proceeds normally.
            feeConfigured: team?.feeConfigured ?? true,
            arbEnrolled: hasLiveArbSubscription(registration),
        };
    }

    /**
     * True when an existing registration is still in deposit phase — eligible
     * for voluntary PIF upgrade at checkout. Includes both unpaid AND paid deposit
     * rows: paid rows true-up to the balance under PIF, unpaid rows pay full.
     * Test: team has deposit and fee configured AND stamped feeBase is below
     * the full (deposit+fee) total.
     */
    /**
     * Resolve a single line's payment phase (per-row, NOT cart-wide):
     *   • existing registration → full when it is NOT in deposit phase (stamped FeeBase has
     *     reached the full deposit+fee, i.e. !isExistingDepositPhase) — the same per-row signal
     *     the server display shaper uses.
     *   • new selection (no reg yet) → the team's server-resolved fullPaymentRequired flag
     *     (ResolveFullPaymentPhase: per-scope override ?? job baseline).
     * A full line is always billed in full; a deposit-eligible line follows the radio.
     */
    private isLineFullPhase(
        teamId: string,
        registration: { financials?: RegistrationFinancialsDto | null } | null | undefined,
        team: { fullPaymentRequired?: boolean | null } | null | undefined,
    ): boolean {
        const financials = registration?.financials;
        if (financials) return !this.isExistingDepositPhase(teamId, financials);
        return team?.fullPaymentRequired === true;
    }

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
    ): { feeBase: number; feeProcessing: number; feeTotal: number; amount: number; echeckAmount: number; checkAmount: number } | null {
        if (!financials || !phaseExpectsFull) return null;
        const deposit = Number(team?.deposit ?? 0) || 0;
        const fee = Number(team?.fee ?? 0) || 0;
        if (deposit <= 0 || fee <= 0) return null;
        const origBase = toNumber(financials.feeBase);
        const newBase = deposit + fee;
        if (newBase <= origBase) return null;
        const origProc = toNumber(financials.feeProcessing);
        const lateFee = toNumber(financials.feeLateFee);
        const discount = toNumber(financials.feeDiscount);
        // origProc is calculated on (origBase - discount), so scale using net bases to
        // preserve the proc rate — otherwise a discount makes the ratio use undiscounted
        // origBase against a discounted origProc, understating the PIF proc.
        const origNetBase = origBase - discount;
        const procRatio = origNetBase > 0 ? (newBase - discount) / origNetBase : 1;
        const newProc = origProc * procRatio;
        const paid = toNumber(financials.paidTotal);
        const total = newBase + newProc + lateFee - discount;
        const amount = Math.max(0, total - paid);
        // Method-correct upgrade quotes. The server realizes a PIF upgrade at the CC rate and
        // then backs the method's proc credit out of the debit (ResolveOwed / AppliedProcCredit
        // — eCheck drops the CC−eCheck rate spread, check the full CC proc). Mirror: the
        // upgrade's method saving = the saving the server already computed for the CURRENT
        // stamp (owed − echeck/checkOwedTotal) + the fresh principal this upgrade adds
        // (newBase − origBase, all unpaid, its proc levied at the CC rate) credited at the
        // method's rate. Without this the PIF radio, the deposit radio's "due later", the
        // accounting table under PIF, and the eCheck expectedTotal all quote the CC figure
        // under eCheck/check — and the server's shown↔charged guard refuses the debit.
        const curOwed = this.getAmountFromFinancials(financials);
        const addedBase = newBase - origBase;
        let echeckAmount = amount;
        let checkAmount = amount;
        if (this.jobCtx.bAddProcessingFees() && addedBase > 0) {
            const ccRate = this.jobCtx.effectiveProcessingRate();
            const echeckRate = this.jobCtx.effectiveEcheckProcessingRate();
            const curEcheckSaving = Math.max(0, curOwed - toNumber(financials.echeckOwedTotal));
            const curCheckSaving = Math.max(0, curOwed - toNumber(financials.checkOwedTotal));
            echeckAmount = Math.max(0, amount - roundCents(curEcheckSaving + addedBase * Math.max(0, ccRate - echeckRate)));
            checkAmount = Math.max(0, amount - roundCents(curCheckSaving + addedBase * ccRate));
        }
        return {
            feeBase: newBase,
            feeProcessing: newProc,
            feeTotal: total,
            amount,
            echeckAmount,
            checkAmount,
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

    submitPayment(request: PaymentRequestDto): Observable<PaymentResponseDto> {
        return this.http.post<PaymentResponseDto>(`${environment.apiUrl}/player-registration/submit-payment`, request);
    }

    /** eCheck (ACH) sibling of submitPayment — posts to /player-registration/submit-echeck. */
    submitEcheckPayment(request: PaymentRequestDto): Observable<PaymentResponseDto> {
        return this.http.post<PaymentResponseDto>(`${environment.apiUrl}/player-registration/submit-echeck`, request);
    }
}
