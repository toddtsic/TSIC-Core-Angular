import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, tap, catchError, throwError } from 'rxjs';
import type {
    RegisteredTeamDto,
    ApplyTeamDiscountRequestDto, ApplyTeamDiscountResponseDto,
    TeamPaymentResponseDto,
    TeamArbTrialPaymentRequestDto, TeamArbTrialPaymentResponseDto,
} from '@core/api';
import { environment } from '@environments/environment';
import { formatHttpError } from '../../shared/utils/error-utils';

export interface TeamLineItem {
    teamId: string;
    teamName: string;
    ageGroup: string;
    levelOfPlay: string | null;
    registrationTs: string;
    feeBase: number;
    feeProcessingDue: number;
    feeTotal: number;
    paidTotal: number;
    depositDue: number;
    additionalDue: number;
    owedTotal: number;
    ccOwedTotal: number;
    ckOwedTotal: number;
    ekOwedTotal: number;
}

/**
 * Team payment calculation service.
 * Computes line items, totals, and balance due for registered teams.
 * Handles payment method selection (CC vs Check) and conditional fee display.
 * Requires teams signal and metadata signal to be set externally (by component).
 */
@Injectable({ providedIn: 'root' })
export class TeamPaymentService {
    private readonly http = inject(HttpClient);

    // --- Signal encapsulation: private backing + public readonly + controlled mutators ---
    private readonly _teams = signal<RegisteredTeamDto[]>([]);
    /** False until the first setTeams() lands. Lets the payment screen hold a spinner instead of
     *  flashing "No payment due" while the async teams/balance fetch is still in flight (PL-038). */
    private readonly _teamsLoaded = signal<boolean>(false);
    private readonly _paymentMethodsAllowedCode = signal<number>(1);
    private readonly _bAddProcessingFees = signal<boolean>(false);
    private readonly _bApplyProcessingFeesToTeamDeposit = signal<boolean>(false);
    /** Per-job opt-in for eCheck (ACH). Independent of paymentMethodsAllowedCode. */
    private readonly _bEnableEcheck = signal<boolean>(false);
    /** Per-job opt-in for ARB-Trial scheduled payments (deposit tomorrow, balance on configured date). */
    private readonly _adnArbTrial = signal<boolean>(false);
    /** Configured balance-charge date for ARB-Trial subscriptions (ISO yyyy-MM-dd). Null when not set. */
    private readonly _adnStartDateAfterTrial = signal<string | null>(null);
    private readonly _jobPath = signal<string>('');
    private readonly _selectedPaymentMethod = signal<'CC' | 'Echeck' | 'ArbTrial' | 'Check'>('CC');
    /**
     * When ARB-Trial is the selected method, this picks which payment instrument
     * the trial subscription will be funded by (card vs bank). Independent of the
     * top-level method tile — we keep it on a separate signal so toggling between
     * "Pay Now" and "ARB-Trial" doesn't lose the rep's CC/eCheck pick.
     */
    private readonly _arbTrialSource = signal<'CC' | 'Echeck'>('CC');
    private readonly _payTo = signal<string | null>(null);
    private readonly _mailTo = signal<string | null>(null);
    private readonly _mailinPaymentWarning = signal<string | null>(null);
    private readonly _appliedDiscountResponse = signal<ApplyTeamDiscountResponseDto | null>(null);
    private readonly _discountMessage = signal<string>('');
    private readonly _discountApplying = signal<boolean>(false);
    /** Optional donor-entered gift (principal). Repriced client-side off the server-supplied
     *  effective rates; sent alongside a donation-inclusive totalAmount so the server's
     *  AMOUNT_MISMATCH tripwire (which re-stamps + recomputes the gift) stays quiet. */
    private readonly _donation = signal(0);
    private readonly _bIncludeTeamDonation = signal(false);
    private readonly _effectiveCcRate = signal(0);
    private readonly _effectiveEcheckRate = signal(0);

    readonly teams = this._teams.asReadonly();
    readonly teamsLoaded = this._teamsLoaded.asReadonly();
    readonly paymentMethodsAllowedCode = this._paymentMethodsAllowedCode.asReadonly();
    readonly bAddProcessingFees = this._bAddProcessingFees.asReadonly();
    readonly bApplyProcessingFeesToTeamDeposit = this._bApplyProcessingFeesToTeamDeposit.asReadonly();
    readonly bEnableEcheck = this._bEnableEcheck.asReadonly();
    readonly adnArbTrial = this._adnArbTrial.asReadonly();
    readonly adnStartDateAfterTrial = this._adnStartDateAfterTrial.asReadonly();
    readonly jobPath = this._jobPath.asReadonly();
    readonly selectedPaymentMethod = this._selectedPaymentMethod.asReadonly();
    readonly arbTrialSource = this._arbTrialSource.asReadonly();
    readonly payTo = this._payTo.asReadonly();
    readonly mailTo = this._mailTo.asReadonly();
    readonly mailinPaymentWarning = this._mailinPaymentWarning.asReadonly();
    readonly appliedDiscountResponse = this._appliedDiscountResponse.asReadonly();
    readonly discountMessage = this._discountMessage.asReadonly();
    readonly discountApplying = this._discountApplying.asReadonly();
    readonly discountAppliedOk = computed(() => {
        const resp = this._appliedDiscountResponse();
        return !!resp?.success && resp.successCount > 0;
    });
    readonly donation = this._donation.asReadonly();
    /** True when the job offers an optional donation field on the team payment page. */
    readonly bIncludeTeamDonation = this._bIncludeTeamDonation.asReadonly();
    setDonation(v: number | string): void {
        const n = typeof v === 'string' ? Number.parseFloat(v) : v;
        this._donation.set(Number.isFinite(n) && n > 0 ? Math.round(n * 100) / 100 : 0);
    }
    resetDonation(): void { this._donation.set(0); }

    /** Donation's CC-path contribution: principal + CC processing, at the same effective rate the
     *  server charges and rounded the same way, so the submitted total matches serverTotal.
     *  The 1e-7 nudge mirrors the server's decimal AwayFromZero at true half-cent midpoints,
     *  which binary doubles store just BELOW the boundary (e.g. 2.845 → 2.84499…). */
    readonly donationCc = computed(() => {
        const d = this._donation();
        if (d <= 0) return 0;
        return d + (this._bAddProcessingFees() ? Math.round(d * this._effectiveCcRate() * 100 + 1e-7) / 100 : 0);
    });
    /** Donation's eCheck-path contribution: principal + eCheck processing (the lower ACH rate). */
    readonly donationEcheck = computed(() => {
        const d = this._donation();
        if (d <= 0) return 0;
        return d + (this._bAddProcessingFees() ? Math.round(d * this._effectiveEcheckRate() * 100 + 1e-7) / 100 : 0);
    });
    /** Just the CC processing levied on the donation — for the help-text breakdown. */
    readonly donationProcessing = computed(() => Math.max(0, this.donationCc() - this._donation()));

    // Controlled mutators
    setTeams(value: RegisteredTeamDto[]): void { this._teams.set(value); this._teamsLoaded.set(true); }
    setPaymentConfig(code: number, addFees: boolean, applyToDeposit: boolean,
        payTo?: string | null, mailTo?: string | null, mailinPaymentWarning?: string | null,
        enableEcheck = false, adnArbTrial = false, adnStartDateAfterTrial: string | null = null,
        includeTeamDonation = false, ccRate = 0, echeckRate = 0): void {
        this._paymentMethodsAllowedCode.set(code);
        this._bAddProcessingFees.set(addFees);
        this._bApplyProcessingFeesToTeamDeposit.set(applyToDeposit);
        this._payTo.set(payTo ?? null);
        this._mailTo.set(mailTo ?? null);
        this._mailinPaymentWarning.set(mailinPaymentWarning ?? null);
        this._bEnableEcheck.set(enableEcheck);
        this._adnArbTrial.set(adnArbTrial);
        this._adnStartDateAfterTrial.set(adnStartDateAfterTrial);
        this._bIncludeTeamDonation.set(includeTeamDonation);
        this._effectiveCcRate.set(ccRate);
        this._effectiveEcheckRate.set(echeckRate);
        // Default to the first visible button in priority order CC > Echeck > Check
        // so we never land on a hidden method (e.g. check-only + eCheck enabled).
        // ArbTrial is opt-in, so we don't auto-select it on load.
        if (code !== 3) {
            this._selectedPaymentMethod.set('CC');
        } else if (enableEcheck) {
            this._selectedPaymentMethod.set('Echeck');
        } else {
            this._selectedPaymentMethod.set('Check');
        }
        // ARB-Trial source defaults to CC unless the only available source is eCheck.
        this._arbTrialSource.set(code !== 3 ? 'CC' : (enableEcheck ? 'Echeck' : 'CC'));
    }
    setJobPath(value: string): void { this._jobPath.set(value); }
    selectPaymentMethod(method: 'CC' | 'Echeck' | 'ArbTrial' | 'Check'): void { this._selectedPaymentMethod.set(method); }
    selectArbTrialSource(src: 'CC' | 'Echeck'): void { this._arbTrialSource.set(src); }

    // Line items for all registered teams
    lineItems = computed<TeamLineItem[]>(() => {
        const teams = this.teams();
        return teams.map(t => ({
            teamId: t.teamId,
            teamName: t.teamName,
            ageGroup: t.ageGroupName || '',
            levelOfPlay: t.levelOfPlay,
            registrationTs: t.registrationTs,
            feeBase: t.feeBase ?? 0,
            feeProcessingDue: t.feeProcessingDue ?? 0,
            feeTotal: t.feeTotal ?? 0,
            paidTotal: t.paidTotal ?? 0,
            depositDue: t.depositDue ?? 0,
            additionalDue: t.additionalDue ?? 0,
            owedTotal: t.owedTotal ?? 0,
            ccOwedTotal: t.ccOwedTotal ?? 0,
            ckOwedTotal: t.ckOwedTotal ?? 0,
            ekOwedTotal: t.ekOwedTotal ?? 0
        }));
    });

    // Total fees across all teams
    totalFees = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeTotal, 0));
    totalFeeBase = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeBase, 0));
    totalFeeProcessingDue = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeProcessingDue, 0));

    // Total already paid
    totalPaid = computed(() => this.lineItems().reduce((sum, item) => sum + item.paidTotal, 0));

    // Balance due (owed)
    balanceDue = computed(() => this.lineItems().reduce((sum, item) => sum + item.owedTotal, 0));
    totalCcOwed = computed(() => this.lineItems().reduce((sum, item) => sum + item.ccOwedTotal, 0));
    totalCkOwed = computed(() => this.lineItems().reduce((sum, item) => sum + item.ckOwedTotal, 0));
    totalEkOwed = computed(() => this.lineItems().reduce((sum, item) => sum + item.ekOwedTotal, 0));

    // Amount to charge by selected method. These are server-computed per-team totals
    // (PaymentState is the single source of truth) — the client never reconstructs the
    // rate math. eCheck is its OWN total now: the backend debits the eCheck gross, not
    // the CC gross, so submitting totalCcOwed for eCheck would trip AMOUNT_MISMATCH.
    //   • CC     → totalCcOwed  (full CC proc baked in)
    //   • eCheck → totalEkOwed  (lower eCheck proc rate; engine debits this exact figure)
    //   • Check  → totalCkOwed  (no processing fee)
    amountToCharge = computed(() => {
        switch (this.selectedPaymentMethod()) {
            case 'Check': return this.totalCkOwed();
            case 'Echeck': return this.totalEkOwed() + this.donationEcheck();
            default: return this.totalCcOwed() + this.donationCc();
        }
    });

    // Whether payment is required
    hasBalance = computed(() => this.balanceDue() > 0);

    // Team IDs with outstanding balance
    teamIdsWithBalance = computed(() =>
        this.lineItems()
            .filter(item => item.owedTotal > 0)
            .map(item => item.teamId)
    );

    // Payment method state
    isCheckPayment = computed(() => this.selectedPaymentMethod() === 'Check');
    isCcPayment = computed(() => this.selectedPaymentMethod() === 'CC');
    isEcheckPayment = computed(() => this.selectedPaymentMethod() === 'Echeck');
    isArbTrialPayment = computed(() => this.selectedPaymentMethod() === 'ArbTrial');
    isCheckOnly = computed(() => this.paymentMethodsAllowedCode() === 3);

    // Method visibility:
    //   • CC button: shown unless the job is check-only (code 3).
    //   • eCheck button: per-job opt-in.
    //   • ARB-Trial button: per-job opt-in AND a configured balance date AND at least one
    //     electronic source enabled (CC unless check-only, or eCheck when opted in).
    //   • Mail-in Check button: hidden when eCheck is enabled — eCheck is the
    //     online replacement for paper check, so admins shouldn't offer both.
    showCcButton = computed(() => this.paymentMethodsAllowedCode() !== 3);
    showEcheckButton = computed(() => this.bEnableEcheck());
    showArbTrialButton = computed(() => {
        if (!this.adnArbTrial()) return false;
        if (!this.adnStartDateAfterTrial()) return false;
        // Past the balance date there is no plan left to schedule — don't offer one.
        // (The backend rejects ARB_TRIAL_WINDOW_PASSED as a stale-client guard.)
        if (this.arbTrialIsFallback()) return false;
        // Need at least one electronic source — check-only jobs without eCheck can't run ARB-Trial.
        return this.showCcButton() || this.showEcheckButton();
    });
    showCheckButton = computed(() =>
        this.paymentMethodsAllowedCode() !== 1 && !this.bEnableEcheck()
    );

    /**
     * True when today is on/after the configured balance date — there is no future
     * installment left to schedule, so the ARB-Trial option is not offered at all
     * (gates showArbTrialButton; the backend rejects ARB_TRIAL_WINDOW_PASSED as a
     * stale-client guard). The rep pays via the regular CC/eCheck buttons.
     */
    arbTrialIsFallback = computed(() => {
        const balanceIso = this.adnStartDateAfterTrial();
        if (!balanceIso) return false;
        const balance = new Date(balanceIso);
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        balance.setHours(0, 0, 0, 0);
        return today.getTime() >= balance.getTime();
    });

    /** ISO date string for the deposit charge (always today + 1 day). */
    arbTrialDepositDate = computed(() => {
        const tomorrow = new Date();
        tomorrow.setHours(0, 0, 0, 0);
        tomorrow.setDate(tomorrow.getDate() + 1);
        return tomorrow.toISOString().slice(0, 10);
    });

    // Column visibility flags. Selector appears when more than one method is available
    // (eCheck and ARB-Trial each add an option even on CC-only jobs).
    showPaymentMethodSelector = computed(() =>
        (this.showCcButton() ? 1 : 0)
        + (this.showEcheckButton() ? 1 : 0)
        + (this.showArbTrialButton() ? 1 : 0)
        + (this.showCheckButton() ? 1 : 0) >= 2
    );
    showFeeProcessingColumn = computed(() => this.bAddProcessingFees());
    showCcOwedColumn = computed(() => this.paymentMethodsAllowedCode() !== 3); // Hide if Check only
    showCkOwedColumn = computed(() =>
        this.paymentMethodsAllowedCode() !== 1 && this.bAddProcessingFees()
    ); // Hide if CC only OR no processing fees

    // Table colspan calculation for "Amount to Charge" row
    getColspan = computed(() => {
        let cols = 3; // Base: Team Name, Age Group, Already Paid
        if (this.showFeeProcessingColumn()) cols++;
        if (this.showCcOwedColumn()) cols++;
        if (this.showCkOwedColumn()) cols++;
        return cols;
    });

    /**
     * Apply discount code to selected teams.
     * Makes HTTP POST to /api/team-registration/apply-discount.
     * Updates discount signals with per-team results.
     */
    applyDiscount(code: string, teamIds: string[]): Observable<ApplyTeamDiscountResponseDto> {
        if (!code || this.discountApplying()) {
            return throwError(() => new Error('Invalid discount code or already applying'));
        }

        if (teamIds.length === 0) {
            return throwError(() => new Error('No teams selected for discount'));
        }

        this._discountApplying.set(true);
        this._discountMessage.set('');

        const request: ApplyTeamDiscountRequestDto = {
            jobPath: this.jobPath(),
            code,
            teamIds: teamIds as string[] // GUID strings accepted by API
        };

        return this.http.post<ApplyTeamDiscountResponseDto>(
            `${environment.apiUrl}/team-registration/apply-discount`,
            request
        ).pipe(
            tap((resp: ApplyTeamDiscountResponseDto) => {
                this._discountApplying.set(false);
                this._appliedDiscountResponse.set(resp);

                if (resp.success && resp.successCount > 0) {
                    this._discountMessage.set(resp.message || 'Discount applied');
                } else {
                    this._discountMessage.set(resp.message || 'Failed to apply discount');
                }
            }),
            catchError((err: HttpErrorResponse) => {
                this._discountApplying.set(false);
                this._appliedDiscountResponse.set(null);
                this._discountMessage.set(formatHttpError(err));
                throw err;
            })
        );
    }

    /**
     * Reset discount state
     */
    resetDiscount(): void {
        this._appliedDiscountResponse.set(null);
        this._discountMessage.set('');
        this._discountApplying.set(false);
    }

    /**
     * Submit team payment to the backend.
     * Extracted from TeamPaymentStepComponent to keep HTTP calls in the service layer.
     */
    submitPayment(request: Record<string, unknown>): Observable<TeamPaymentResponseDto> {
        return this.http.post<TeamPaymentResponseDto>(
            `${environment.apiUrl}/team-payment/process`,
            request
        );
    }

    /** eCheck (ACH) sibling of submitPayment — posts to /team-payment/process-echeck. */
    submitEcheckPayment(request: Record<string, unknown>): Observable<TeamPaymentResponseDto> {
        return this.http.post<TeamPaymentResponseDto>(
            `${environment.apiUrl}/team-payment/process-echeck`,
            request
        );
    }

    /**
     * ARB-Trial submission. Backend creates one ADN ARB subscription per team
     * (deposit tomorrow, balance on the configured date). Capture-what-you-can:
     * partial-success responses still carry the per-team result rows the UI renders.
     */
    submitArbTrialPayment(request: TeamArbTrialPaymentRequestDto): Observable<TeamArbTrialPaymentResponseDto> {
        return this.http.post<TeamArbTrialPaymentResponseDto>(
            `${environment.apiUrl}/team-payment/process-arb-trial`,
            request
        );
    }

    reset(): void {
        this._teams.set([]);
        this._teamsLoaded.set(false);
        this._paymentMethodsAllowedCode.set(1);
        this._bAddProcessingFees.set(false);
        this._bApplyProcessingFeesToTeamDeposit.set(false);
        this._bEnableEcheck.set(false);
        this._adnArbTrial.set(false);
        this._adnStartDateAfterTrial.set(null);
        this._donation.set(0);
        this._bIncludeTeamDonation.set(false);
        this._effectiveCcRate.set(0);
        this._effectiveEcheckRate.set(0);
        this._selectedPaymentMethod.set('CC');
        this._arbTrialSource.set('CC');
        this._jobPath.set('');
        this._payTo.set(null);
        this._mailTo.set(null);
        this._mailinPaymentWarning.set(null);
        this.resetDiscount();
    }
}
