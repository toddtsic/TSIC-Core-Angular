import { Injectable, inject } from '@angular/core';
import { RegistrationWizardService, PaymentOption } from '../registration-wizard.service';

/**
 * Facade slice for payment-related wizard state (option selection + last payment summary).
 * Phase 1: proxy underlying signals from RegistrationWizardService.
 * Future Phase: migrate internal representation fully and shrink RegistrationWizardService surface.
 */
export interface PaymentSummary {
    option: PaymentOption;
    amount: number;
    transactionId?: string;
    subscriptionId?: string;
    viPolicyNumber?: string | null;
    viPolicyCreateDate?: string | null;
    message?: string | null;
}

@Injectable({ providedIn: 'root' })
export class PaymentStateService {
    private readonly reg = inject(RegistrationWizardService);

    // Accessors ----------------------------------------------------------------
    paymentOption(): PaymentOption { return this.reg.paymentOption(); }
    lastPayment(): PaymentSummary | null { return this.reg.lastPayment(); }

    // Mutators -----------------------------------------------------------------
    setPaymentOption(opt: PaymentOption): void { this.reg.paymentOption.set(opt); }
    setLastPayment(summary: PaymentSummary | null): void { this.reg.lastPayment.set(summary); }
}
