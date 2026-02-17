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

    // Teams signal - must be set by the component that owns team state
    teams = signal<RegisteredTeamDto[]>([]);

    // Metadata signals - payment configuration
    paymentMethodsAllowedCode = signal<number>(1); // 1=CC only, 2=Both, 3=Check only
    bAddProcessingFees = signal<boolean>(false);
    bApplyProcessingFeesToTeamDeposit = signal<boolean>(false);
    jobPath = signal<string>('');

    // Selected payment method signal
    selectedPaymentMethod = signal<'CC' | 'Check'>('CC');

    // Discount signals
    appliedDiscountResponse = signal<ApplyTeamDiscountResponseDto | null>(null);
    discountMessage = signal<string>('');
    discountApplying = signal<boolean>(false);

    // Line items for all registered teams
    lineItems = computed<TeamLineItem[]>(() => {
        const teams = this.teams();
        return teams.map((t: any) => ({
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

        this.discountApplying.set(true);
        this.discountMessage.set('');

        const request: ApplyTeamDiscountRequestDto = {
            jobPath: this.jobPath(),
            code,
            teamIds: teamIds.map(id => id as any) // Convert string to Guid
        };

        return this.http.post<ApplyTeamDiscountResponseDto>(
            `${environment.apiUrl}/team-registration/apply-discount`,
            request
        ).pipe(
            tap((resp: ApplyTeamDiscountResponseDto) => {
                this.discountApplying.set(false);
                this.appliedDiscountResponse.set(resp);

                if (resp.success && resp.successCount > 0) {
                    this.discountMessage.set(resp.message || 'Discount applied');
                } else {
                    this.discountMessage.set(resp.message || 'Failed to apply discount');
                }
            }),
            catchError((err: HttpErrorResponse) => {
                this.discountApplying.set(false);
                this.appliedDiscountResponse.set(null);
                this.discountMessage.set(err?.error?.message || err?.message || 'Failed to apply discount code');
                throw err;
            })
        );
    }

    /**
     * Reset discount state
     */
    resetDiscount(): void {
        this.appliedDiscountResponse.set(null);
        this.discountMessage.set('');
        this.discountApplying.set(false);
    }

    /**
     * Submit team payment to the backend.
     * Extracted from TeamPaymentStepComponent to keep HTTP calls in the service layer.
     */
    submitPayment(request: Record<string, any>): Observable<TeamPaymentResponseDto> {
        return this.http.post<TeamPaymentResponseDto>(
            `${environment.apiUrl}/team-payment/process`,
            request
        );
    }

    reset(): void {
        this.teams.set([]);
        this.paymentMethodsAllowedCode.set(1);
        this.bAddProcessingFees.set(false);
        this.bApplyProcessingFeesToTeamDeposit.set(false);
        this.selectedPaymentMethod.set('CC');
        this.jobPath.set('');
        this.resetDiscount();
    }
}
