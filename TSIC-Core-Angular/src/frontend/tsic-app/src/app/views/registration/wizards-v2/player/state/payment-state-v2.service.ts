import { Injectable, inject } from '@angular/core';
import { JobContextService } from './job-context.service';
import { PlayerWizardStateService } from './player-wizard-state.service';
import type { PaymentOption, PaymentSummary } from '../types/player-wizard.types';

/**
 * Payment State v2 — same public API as PaymentStateService,
 * but reads from decomposed JobContextService + PlayerWizardStateService
 * instead of RegistrationWizardService.
 */
@Injectable({ providedIn: 'root' })
export class PaymentStateV2Service {
    private readonly jobCtx = inject(JobContextService);
    private readonly wizardState = inject(PlayerWizardStateService);

    paymentOption(): PaymentOption { return this.jobCtx.paymentOption(); }
    lastPayment(): PaymentSummary | null { return this.wizardState.lastPayment(); }

    setPaymentOption(opt: PaymentOption): void { this.jobCtx.setPaymentOption(opt); }
    setLastPayment(summary: PaymentSummary | null): void { this.wizardState.setLastPayment(summary); }
}
