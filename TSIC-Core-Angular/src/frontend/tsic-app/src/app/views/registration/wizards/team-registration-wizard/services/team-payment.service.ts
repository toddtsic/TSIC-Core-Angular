import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, tap, catchError, throwError } from 'rxjs';
import type { RegisteredTeamDto, ApplyTeamDiscountRequestDto, ApplyTeamDiscountResponseDto, TeamPaymentResponseDto } from '@core/api';
import { environment } from '@environments/environment';

export interface TeamLineItem {
    teamId: string;
    teamName: string;
    ageGroup: string;
    levelOfPlay: string | null;
    registrationTs: string;
    feeBase: number;
    feeProcessing: number;
    feeTotal: number;
    paidTotal: number;
    depositDue: number;
    additionalDue: number;
    owedTotal: number;
    ccOwedTotal: number;
    ckOwedTotal: number;
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
    private readonly _paymentMethodsAllowedCode = signal<number>(1);
    private readonly _bAddProcessingFees = signal<boolean>(false);
    private readonly _bApplyProcessingFeesToTeamDeposit = signal<boolean>(false);
    private readonly _jobPath = signal<string>('');
    private readonly _selectedPaymentMethod = signal<'CC' | 'Check'>('CC');
    private readonly _appliedDiscountResponse = signal<ApplyTeamDiscountResponseDto | null>(null);
    private readonly _discountMessage = signal<string>('');
    private readonly _discountApplying = signal<boolean>(false);

    readonly teams = this._teams.asReadonly();
    readonly paymentMethodsAllowedCode = this._paymentMethodsAllowedCode.asReadonly();
    readonly bAddProcessingFees = this._bAddProcessingFees.asReadonly();
    readonly bApplyProcessingFeesToTeamDeposit = this._bApplyProcessingFeesToTeamDeposit.asReadonly();
    readonly jobPath = this._jobPath.asReadonly();
    readonly selectedPaymentMethod = this._selectedPaymentMethod.asReadonly();
    readonly appliedDiscountResponse = this._appliedDiscountResponse.asReadonly();
    readonly discountMessage = this._discountMessage.asReadonly();
    readonly discountApplying = this._discountApplying.asReadonly();

    // Controlled mutators
    setTeams(value: RegisteredTeamDto[]): void { this._teams.set(value); }
    setPaymentConfig(code: number, addFees: boolean, applyToDeposit: boolean): void {
        this._paymentMethodsAllowedCode.set(code);
        this._bAddProcessingFees.set(addFees);
        this._bApplyProcessingFeesToTeamDeposit.set(applyToDeposit);
    }
    setJobPath(value: string): void { this._jobPath.set(value); }
    selectPaymentMethod(method: 'CC' | 'Check'): void { this._selectedPaymentMethod.set(method); }

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
            feeProcessing: t.feeProcessing ?? 0,
            feeTotal: t.feeTotal ?? 0,
            paidTotal: t.paidTotal ?? 0,
            depositDue: t.depositDue ?? 0,
            additionalDue: t.additionalDue ?? 0,
            owedTotal: t.owedTotal ?? 0,
            ccOwedTotal: t.ccOwedTotal ?? 0,
            ckOwedTotal: t.ckOwedTotal ?? 0
        }));
    });

    // Total fees across all teams
    totalFees = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeTotal, 0));
    totalFeeBase = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeBase, 0));
    totalFeeProcessing = computed(() => this.lineItems().reduce((sum, item) => sum + item.feeProcessing, 0));

    // Total already paid
    totalPaid = computed(() => this.lineItems().reduce((sum, item) => sum + item.paidTotal, 0));

    // Balance due (owed)
    balanceDue = computed(() => this.lineItems().reduce((sum, item) => sum + item.owedTotal, 0));
    totalCcOwed = computed(() => this.lineItems().reduce((sum, item) => sum + item.ccOwedTotal, 0));
    totalCkOwed = computed(() => this.lineItems().reduce((sum, item) => sum + item.ckOwedTotal, 0));

    // Amount to charge based on selected payment method
    amountToCharge = computed(() =>
        this.selectedPaymentMethod() === 'Check' ? this.totalCkOwed() : this.totalCcOwed()
    );

    // Processing fee savings when paying by check
    processingFeeSavings = computed(() => this.totalCcOwed() - this.totalCkOwed());

    // Whether payment is required
    hasBalance = computed(() => this.balanceDue() > 0);

    // Team IDs with outstanding balance
    teamIdsWithBalance = computed(() =>
        this.lineItems()
            .filter(item => item.owedTotal > 0)
            .map(item => item.teamId)
    );

    // Column visibility flags
    showPaymentMethodSelector = computed(() => this.paymentMethodsAllowedCode() === 2);
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
                this._discountMessage.set(err?.error?.message || err?.message || 'Failed to apply discount code');
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

    reset(): void {
        this._teams.set([]);
        this._paymentMethodsAllowedCode.set(1);
        this._bAddProcessingFees.set(false);
        this._bApplyProcessingFeesToTeamDeposit.set(false);
        this._selectedPaymentMethod.set('CC');
        this._jobPath.set('');
        this.resetDiscount();
    }
}
