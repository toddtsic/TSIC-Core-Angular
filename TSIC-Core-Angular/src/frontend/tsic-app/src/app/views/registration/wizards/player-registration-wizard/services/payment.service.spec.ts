import { TestBed } from '@angular/core/testing';
import { PaymentService } from './payment.service';
import { RegistrationWizardService } from '../registration-wizard.service';
import { PlayerStateService } from './player-state.service';
import { TeamService } from '../team.service';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';

// Minimal stubs for dependent services
class RegistrationWizardStub {
    jobId = () => 'JOB1';
    jobPath = () => 'testjob';
    familyUser = () => ({ familyUserId: 'FAM1' });
    loadFamilyPlayersOnce = () => Promise.resolve();
    paymentOptionSig: any = 'Full';
    paymentOption = () => this.paymentOptionSig;
    // Simulate selected players
    familyPlayers = () => [
        { playerId: 'P1', firstName: 'Alice', lastName: 'A', selected: true, registered: false },
        { playerId: 'P2', firstName: 'Bob', lastName: 'B', selected: true, registered: false }
    ];
    private _selectedTeams: Record<string, string | string[]> = { P1: 'T1', P2: 'T1' };
    // Will be replaced in constructor with a function carrying a .set method
    selectedTeams: any;
    adnArb: any = () => null;
    adnArbBillingOccurences = () => 0;
    adnArbIntervalLength = () => 1;
    adnArbStartDate = () => null;
    verticalInsureConfirmed = () => false;
    verticalInsureDeclined = () => false;
    constructor() {
        const fn: any = () => this._selectedTeams;
        fn.set = (v: Record<string, string | string[]>) => { this._selectedTeams = { ...v }; };
        this.selectedTeams = fn;
    }
}
class TeamServiceStub {
    getTeamById(id: string) {
        if (id === 'T1') return { teamName: 'Team One', perRegistrantFee: 150, perRegistrantDeposit: 50 };
        if (id === 'T2') return { teamName: 'Team Two', perRegistrantFee: 166.5, perRegistrantDeposit: 0 };
        return null;
    }
}

describe('PaymentService', () => {
    let service: PaymentService;
    let httpMock: HttpTestingController;
    let wizard: RegistrationWizardStub;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                PaymentService,
                { provide: RegistrationWizardService, useClass: RegistrationWizardStub },
                PlayerStateService,
                { provide: TeamService, useClass: TeamServiceStub },
                provideHttpClient(),
                provideHttpClientTesting()
            ]
        });
        service = TestBed.inject(PaymentService);
        httpMock = TestBed.inject(HttpTestingController);
        wizard = TestBed.inject(RegistrationWizardService) as any;
        // Seed PlayerStateService with initial selected teams since it is now authoritative
        const ps = TestBed.inject(PlayerStateService);
        ps.setSelectedTeams(wizard.selectedTeams());
    });

    afterEach(() => httpMock.verify());

    it('builds lineItems for selected players', () => {
        const items = service.lineItems();
        expect(items.length).toBe(2);
        expect(items[0].playerName).toContain('Alice');
        expect(items[1].amount).toBe(150);
    });

    it('computes totalAmount correctly', () => {
        expect(service.totalAmount()).toBe(300);
    });

    it('computes depositTotal correctly', () => {
        wizard.paymentOptionSig = 'Deposit';
        // depositTotal sums deposit per player
        expect(service.depositTotal()).toBe(100); // 50 * 2
    });

    it('applies discount and updates discountMessage', () => {
        wizard.paymentOptionSig = 'Full';
        service.applyDiscount('SAVE10');
        const req = httpMock.expectOne(`${environment.apiUrl}/player-registration/apply-discount`);
        expect(req.request.method).toBe('POST');
        req.flush({ success: true, totalDiscount: 25, message: 'Discount applied' });
        // Service relies on refreshed financials rather than local discount signal
        expect(service.discountMessage()).toBe('Discount applied');
    });

    it('handles failed discount', () => {
        service.applyDiscount('BADCODE');
        const req = httpMock.expectOne(`${environment.apiUrl}/player-registration/apply-discount`);
        req.flush({ success: false, message: 'Invalid' });
        expect(service.appliedDiscount()).toBe(0);
        expect(service.discountMessage()).toContain('Invalid');
    });

    it('identifies ARB scenario and computes per-occurrence values', () => {
        // Switch to ARB scenario
        wizard.adnArb = () => ({ enabled: true });
        wizard.adnArbBillingOccurences = () => 5;
        wizard.adnArbIntervalLength = () => 2;
        expect(service.isArbScenario()).toBe(true);
        expect(service.arbOccurrences()).toBe(5);
        expect(service.monthLabel()).toBe('months');
        // totalAmount remains 300 -> per occurrence 60
        expect(service.arbPerOccurrence()).toBe(60);
    });

    it('rounds arbPerOccurrence to 2 decimals', () => {
        // Use team T2 for players to create non-terminating decimal division (through PlayerStateService)
        const ps = TestBed.inject(PlayerStateService);
        ps.setSelectedTeams({ P1: 'T2', P2: 'T2' });
        wizard.adnArb = () => ({ enabled: true });
        wizard.adnArbBillingOccurences = () => 7; // total 333 / 7 = 47.5714 -> 47.57
        expect(service.totalAmount()).toBe(333);
        expect(service.arbPerOccurrence()).toBe(47.57);
    });

    it('clamps currentTotal at zero when discount exceeds base', () => {
        wizard.paymentOptionSig = 'Full';
        // Directly set appliedDiscount signal to exceed total
        service.appliedDiscount.set(1000);
        expect(service.totalAmount()).toBe(300);
        expect(service.currentTotal()).toBe(0);
    });
});
