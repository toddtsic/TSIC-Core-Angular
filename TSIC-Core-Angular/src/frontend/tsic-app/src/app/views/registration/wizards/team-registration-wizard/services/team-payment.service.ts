import { Injectable, signal, computed } from '@angular/core';
import type { RegisteredTeamDto } from '@core/api';

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
    // Teams signal - must be set by the component that owns team state
    teams = signal<RegisteredTeamDto[]>([]);
    
    // Metadata signals - payment configuration
    paymentMethodsAllowedCode = signal<number>(1); // 1=CC only, 2=Both, 3=Check only
    bAddProcessingFees = signal<boolean>(false);
    bApplyProcessingFeesToTeamDeposit = signal<boolean>(false);
    
    // Selected payment method signal
    selectedPaymentMethod = signal<'CC' | 'Check'>('CC');

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
}
