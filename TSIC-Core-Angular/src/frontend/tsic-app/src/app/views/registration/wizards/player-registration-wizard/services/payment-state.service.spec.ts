import { TestBed } from '@angular/core/testing';
import { PaymentStateService, PaymentSummary } from './payment-state.service';
import { RegistrationWizardService } from '../registration-wizard.service';

class RegistrationWizardStub {
    paymentOptionVal: any = 'PIF';
    lastPaymentVal: any = null;
    constructor() {
        (this.paymentOption as any).set = (v: any) => { this.paymentOptionVal = v; };
        (this.lastPayment as any).set = (v: any) => { this.lastPaymentVal = v; };
    }
    paymentOption() { return this.paymentOptionVal; }
    lastPayment() { return this.lastPaymentVal; }
}

describe('PaymentStateService', () => {
    let svc: PaymentStateService;
    // reg not needed; kept minimal to exercise underlying signals via facade

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                PaymentStateService,
                RegistrationWizardStub,
                { provide: RegistrationWizardService, useExisting: RegistrationWizardStub }
            ]
        });
        svc = TestBed.inject(PaymentStateService);
    });

    it('reads initial payment option', () => {
        expect(['PIF', 'Deposit', 'ARB']).toContain(svc.paymentOption());
    });

    it('sets payment option', () => {
        svc.setPaymentOption('Deposit');
        expect(svc.paymentOption()).toBe('Deposit');
    });

    it('sets last payment summary', () => {
        const summary: PaymentSummary = {
            option: 'PIF',
            amount: 123.45,
            transactionId: 'TX123',
            subscriptionId: undefined,
            viPolicyNumber: null,
            viPolicyCreateDate: null,
            message: 'ok'
        };
        svc.setLastPayment(summary);
        expect(svc.lastPayment()).toEqual(summary);
    });

    it('clears last payment summary', () => {
        svc.setLastPayment({ option: 'PIF', amount: 10 });
        svc.setLastPayment(null);
        expect(svc.lastPayment()).toBeNull();
    });
});
