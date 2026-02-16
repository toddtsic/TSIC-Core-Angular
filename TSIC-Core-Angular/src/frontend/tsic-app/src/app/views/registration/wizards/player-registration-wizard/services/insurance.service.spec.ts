import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { InsuranceService } from './insurance.service';
import { RegistrationWizardService } from '../registration-wizard.service';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';

class RegistrationWizardStub {
    jobId = () => 'JOB1';
    jobPath = () => 'testjob';
    familyUser = () => ({ familyUserId: 'FAM1' });
    familyPlayers = () => [];
    offerPlayerRegSaver = () => true;
    regSaverDetails = () => null;
    verticalInsureOffer = () => ({ loading: false, data: null, error: null });
    verticalInsureConfirmed = () => false;
    verticalInsureDeclined = () => false;
}
class ToastStub { show(_msg?: string, _type?: string, _timeout?: number) { /* noop test stub */ } }

const testCard = { number: '4111111111111111', expiry: '1225', code: '123', firstName: 'Test', lastName: 'User', zip: '12345', email: 'test@test.com' };

describe('InsuranceService', () => {
    let service: InsuranceService;
    let httpMock: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                InsuranceService,
                { provide: RegistrationWizardService, useClass: RegistrationWizardStub },
                { provide: ToastService, useClass: ToastStub },
                provideHttpClient(),
                provideHttpClientTesting()
            ]
        });
        service = TestBed.inject(InsuranceService);
        httpMock = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpMock.verify());

    it('quotedPlayers returns formatted names', () => {
        (service as any).quotes.set([
            { policy_attributes: { participant: { first_name: 'Alice', last_name: 'A' } }, total: 1234 },
            { policy_attributes: { participant: { first_name: 'Bob', last_name: 'B' } }, total: 0 }
        ]);
        const players = service.quotedPlayers();
        expect(players[0]).toBe('Alice A ($12.34)');
        expect(players[1]).toBe('Bob B');
    });

    it('premiumTotal sums cents to dollars', () => {
        (service as any).quotes.set([
            { total: 500 }, { total: 250 }
        ]);
        expect(service.premiumTotal()).toBe(7.5);
    });

    it('purchaseInsurance posts and handles success', () => {
        (service as any).quotes.set([
            { quote_id: 'Q1', metadata: { TsicRegistrationId: 'R1' }, total: 100 },
            { quote_id: 'Q2', metadata: { TsicRegistrationId: 'R2' }, total: 200 }
        ]);
        service.purchaseInsurance(testCard);
        const req = httpMock.expectOne(`${environment.apiUrl}/insurance/purchase`);
        expect(req.request.body.quoteIds.length).toBe(2);
        req.flush({ success: true });
    });

    it('purchaseInsurance handles failure', () => {
        (service as any).quotes.set([
            { quote_id: 'Q1', metadata: { TsicRegistrationId: 'R1' }, total: 100 }
        ]);
        const toast = TestBed.inject(ToastService) as any;
        vi.spyOn(toast, 'show');
        service.purchaseInsurance(testCard);
        const req = httpMock.expectOne(`${environment.apiUrl}/insurance/purchase`);
        req.flush({ success: false, message: 'failed' });
        expect(toast.show).toHaveBeenCalled();
    });
});
