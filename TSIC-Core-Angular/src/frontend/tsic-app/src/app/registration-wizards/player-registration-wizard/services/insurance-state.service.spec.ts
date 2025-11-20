import { TestBed } from '@angular/core/testing';
import { InsuranceStateService } from './insurance-state.service';
import { RegistrationWizardService } from '../registration-wizard.service';

// Lightweight stub avoiding HttpClient dependency
class RegistrationWizardStub {
    private readonly _offer = true;
    private _show = false;
    private _consent: any = null;
    showVerticalInsureModalSig = { set: (v: boolean) => { this._show = v; } };
    offerPlayerRegSaver() { return this._offer; }
    verticalInsureOffer() { return { loading: false, data: null, error: null }; }
    showVerticalInsureModal() { return this._show; }
    viConsent() { return this._consent; }
    hasVerticalInsureDecision() { return !!this._consent; }
    verticalInsureConfirmed() { return !!this._consent?.confirmed; }
    verticalInsureDeclined() { return !!this._consent?.declined; }
    regSaverDetails() { return null; }
    openVerticalInsureModal() { this._show = true; }
    confirmVerticalInsurePurchase(policyNumber: string | null, policyCreateDate: string | null, quotes: any[]) {
        this._consent = { confirmed: true, declined: false, policyNumber, policyCreateDate, quotes };
        this._show = false;
    }
    declineVerticalInsurePurchase() { this._consent = { confirmed: false, declined: true }; this._show = false; }
}

describe('InsuranceStateService', () => {
    let svc: InsuranceStateService;
    // reg reference not required after creation

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                InsuranceStateService,
                RegistrationWizardStub,
                { provide: RegistrationWizardService, useExisting: RegistrationWizardStub }
            ]
        });
        svc = TestBed.inject(InsuranceStateService);
    });

    it('proxies offer flag', () => {
        expect(svc.offerPlayerRegSaver()).toBeTrue();
    });

    it('opens and confirms purchase', () => {
        expect(svc.showVerticalInsureModal()).toBeFalse();
        svc.openVerticalInsureModal();
        expect(svc.showVerticalInsureModal()).toBeTrue();
        svc.confirmVerticalInsurePurchase('PN123', '2025-01-01', [{ id: 'Q1' }]);
        expect(svc.verticalInsureConfirmed()).toBeTrue();
        expect(svc.viConsent()?.policyNumber).toBe('PN123');
    });

    it('declines purchase', () => {
        svc.openVerticalInsureModal();
        svc.declineVerticalInsurePurchase();
        expect(svc.verticalInsureDeclined()).toBeTrue();
        expect(svc.hasVerticalInsureDecision()).toBeTrue();
    });

    it('closes modal without altering consent', () => {
        svc.openVerticalInsureModal();
        expect(svc.showVerticalInsureModal()).toBeTrue();
        svc.closeVerticalInsureModal();
        expect(svc.showVerticalInsureModal()).toBeFalse();
        expect(svc.hasVerticalInsureDecision()).toBeFalse();
    });
});
