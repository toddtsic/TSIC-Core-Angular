import { Injectable, signal } from '@angular/core';

/**
 * State management for team payment step.
 * Tracks payment option selection and last payment result.
 * Note: Teams only support Pay-In-Full (PIF), no ARB/deposits.
 */
export interface TeamPaymentSummary {
    amount: number;
    transactionId?: string;
    viPolicyNumbers?: Record<string, string>; // teamId -> policyNumber
    message?: string | null;
}

@Injectable({ providedIn: 'root' })
export class TeamPaymentStateService {
    // Always PIF for teams (no deposit/ARB options)
    private readonly _paymentOption = signal<'PIF'>('PIF');

    // Result of last payment transaction
    private readonly _lastPayment = signal<TeamPaymentSummary | null>(null);

    // Accessors
    paymentOption() { return this._paymentOption(); }
    lastPayment() { return this._lastPayment(); }

    // Mutators
    setLastPayment(summary: TeamPaymentSummary | null): void {
        this._lastPayment.set(summary);
    }

    reset(): void {
        this._lastPayment.set(null);
    }
}
