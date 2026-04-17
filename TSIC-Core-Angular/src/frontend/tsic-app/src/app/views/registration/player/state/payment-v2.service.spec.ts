import { TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { PaymentV2Service } from './payment-v2.service';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import { PlayerStateService } from '@views/registration/player/services/player-state.service';
import { TeamService, AvailableTeam } from '@views/registration/player/services/team.service';
import type { FamilyPlayerDto, RegistrationFinancialsDto } from '@core/api';

// ── Helpers ──────────────────────────────────────────────────────────

function makeTeam(overrides: Partial<AvailableTeam> & { teamId: string }): AvailableTeam {
    return {
        teamName: 'Team A',
        agegroupId: 'ag1',
        maxRosterSize: 20,
        currentRosterSize: 5,
        rosterIsFull: false,
        jobUsesWaitlists: false,
        fee: 200,
        deposit: 50,
        ...overrides,
    };
}

function makePlayer(overrides: Partial<FamilyPlayerDto> & { playerId: string }): FamilyPlayerDto {
    return {
        firstName: 'John',
        lastName: 'Doe',
        gender: 'M',
        registered: false,
        selected: true,
        priorRegistrations: [],
        ...overrides,
    };
}

function makeFinancials(overrides: Partial<RegistrationFinancialsDto> = {}): RegistrationFinancialsDto {
    return {
        feeBase: 0,
        feeProcessing: 0,
        feeDiscount: 0,
        feeDonation: 0,
        feeLateFee: 0,
        feeTotal: 0,
        owedTotal: 0,
        paidTotal: 0,
        ...overrides,
    };
}

// ── Stub factories ───────────────────────────────────────────────────

function createJobContextStub() {
    return {
        adnArb: signal(false),
        adnArbBillingOccurences: signal<number | null>(null),
        adnArbIntervalLength: signal<number | null>(null),
        adnArbStartDate: signal<string | null>(null),
        paymentOption: signal<'PIF' | 'Deposit' | 'ARB'>('PIF'),
        jobHasActiveDiscountCodes: signal(false),
        jobPath: signal('test-job'),
        resolveApiBase: () => 'http://localhost',
    };
}

function createFamilyPlayersStub() {
    const _familyPlayers = signal<FamilyPlayerDto[]>([]);
    return {
        familyPlayers: _familyPlayers.asReadonly(),
        loadFamilyPlayersOnce: () => Promise.resolve(),
        _set: (players: FamilyPlayerDto[]) => _familyPlayers.set(players),
    };
}

function createPlayerStateStub() {
    const _selectedTeams = signal<Record<string, string | string[]>>({});
    return {
        selectedTeams: () => _selectedTeams(),
        setSelectedTeams: (m: Record<string, string | string[]>) => _selectedTeams.set(m),
        _teamsSignal: _selectedTeams,
    };
}

function createTeamServiceStub() {
    const _teams = new Map<string, AvailableTeam>();
    return {
        getTeamById: (id: string) => _teams.get(id),
        _addTeam: (t: AvailableTeam) => _teams.set(t.teamId, t),
        _clear: () => _teams.clear(),
    };
}

// ── Test Suite ───────────────────────────────────────────────────────

describe('PaymentV2Service', () => {
    let service: PaymentV2Service;
    let jobCtx: ReturnType<typeof createJobContextStub>;
    let fp: ReturnType<typeof createFamilyPlayersStub>;
    let playerState: ReturnType<typeof createPlayerStateStub>;
    let teamSvc: ReturnType<typeof createTeamServiceStub>;

    beforeEach(() => {
        jobCtx = createJobContextStub();
        fp = createFamilyPlayersStub();
        playerState = createPlayerStateStub();
        teamSvc = createTeamServiceStub();

        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(),
                provideHttpClientTesting(),
                PaymentV2Service,
                { provide: JobContextService, useValue: jobCtx },
                { provide: FamilyPlayersService, useValue: fp },
                { provide: PlayerStateService, useValue: playerState },
                { provide: TeamService, useValue: teamSvc },
            ],
        });

        service = TestBed.inject(PaymentV2Service);
    });

    // ─── Line Items ──────────────────────────────────────────────────

    describe('lineItems', () => {
        it('should produce one line item for a single selected player with a team', () => {
            const team = makeTeam({ teamId: 't1', teamName: 'Panthers', fee: 150 });
            teamSvc._addTeam(team);
            fp._set([makePlayer({ playerId: 'p1', firstName: 'Alice', lastName: 'Smith' })]);
            playerState.setSelectedTeams({ p1: 't1' });

            const items = service.lineItems();
            expect(items).toHaveLength(1);
            expect(items[0].playerId).toBe('p1');
            expect(items[0].playerName).toBe('Alice Smith');
            expect(items[0].teamName).toBe('Panthers');
            expect(items[0].amount).toBe(150);
        });

        it('should produce multiple line items for multiple players', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', teamName: 'Panthers', fee: 100 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', teamName: 'Tigers', fee: 200 }));
            fp._set([
                makePlayer({ playerId: 'p1', firstName: 'Alice', lastName: 'A' }),
                makePlayer({ playerId: 'p2', firstName: 'Bob', lastName: 'B' }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });

            const items = service.lineItems();
            expect(items).toHaveLength(2);
            expect(items[0].amount).toBe(100);
            expect(items[1].amount).toBe(200);
        });

        it('should skip players without a team selection', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 100 }));
            fp._set([
                makePlayer({ playerId: 'p1' }),
                makePlayer({ playerId: 'p2' }),
            ]);
            // Only p1 has a team
            playerState.setSelectedTeams({ p1: 't1' });

            const items = service.lineItems();
            expect(items).toHaveLength(1);
            expect(items[0].playerId).toBe('p1');
        });

        it('should skip unselected and unregistered players', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 100 }));
            fp._set([
                makePlayer({ playerId: 'p1', selected: false, registered: false }),
            ]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.lineItems()).toHaveLength(0);
        });

        it('should use financials.owedTotal when player has an existing registration', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 300 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 75 }),
                        formFieldValues: {},
                    }],
                }),
            ]);
            playerState.setSelectedTeams({ p1: 't1' });

            const items = service.lineItems();
            expect(items).toHaveLength(1);
            // Should use owedTotal from financials, not team fee
            expect(items[0].amount).toBe(75);
        });

        it('should fall back to team fee for new players without prior registrations', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 250 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.lineItems()[0].amount).toBe(250);
        });

        it('should default to 100 when team fee is null or zero', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: null }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.lineItems()[0].amount).toBe(100);
        });
    });

    // ─── Payment Scenarios ───────────────────────────────────────────

    describe('payment scenarios', () => {
        beforeEach(() => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 200, deposit: 50 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });
        });

        it('should be PIF only when no ARB and deposit scenario evaluates false', () => {
            // Default: adnArb=false, fee & deposit present but that makes it a deposit scenario
            // Set deposit to 0 so isDepositScenario is false
            teamSvc._clear();
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 200, deposit: 0 }));

            expect(service.isArbScenario()).toBe(false);
            expect(service.isDepositScenario()).toBe(false);
        });

        it('should detect ARB scenario when adnArb is true', () => {
            (jobCtx.adnArb as WritableSignal<boolean>).set(true);

            expect(service.isArbScenario()).toBe(true);
        });

        it('should detect deposit scenario when not ARB and all new players have deposit and fee', () => {
            // adnArb=false, fee=200, deposit=50 — should be deposit scenario
            expect(service.isArbScenario()).toBe(false);
            expect(service.isDepositScenario()).toBe(true);
        });

        it('should give ARB precedence over deposit (isDepositScenario false when ARB)', () => {
            (jobCtx.adnArb as WritableSignal<boolean>).set(true);

            expect(service.isArbScenario()).toBe(true);
            expect(service.isDepositScenario()).toBe(false);
        });
    });

    // ─── Total Calculations ──────────────────────────────────────────

    describe('total calculations', () => {
        it('should sum all line item amounts for totalAmount', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 100 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', fee: 250 }));
            fp._set([
                makePlayer({ playerId: 'p1' }),
                makePlayer({ playerId: 'p2' }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });

            expect(service.totalAmount()).toBe(350);
        });

        it('should only count new player deposits in depositTotal', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 200, deposit: 60 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', fee: 300, deposit: 80 }));
            fp._set([
                // p1 is existing registration (has financials) — deposit should NOT count
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 50 }),
                        formFieldValues: {},
                    }],
                }),
                // p2 is new — deposit should count
                makePlayer({ playerId: 'p2' }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });

            expect(service.depositTotal()).toBe(80);
        });

        it('should compute currentTotal in PIF mode as totalAmount', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 200 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });
            (jobCtx.paymentOption as WritableSignal<string>).set('PIF');

            expect(service.currentTotal()).toBe(200);
        });

        it('should compute currentTotal in Deposit mode as existingBalance + depositTotal', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 200, deposit: 60 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', fee: 300, deposit: 80 }));
            fp._set([
                // p1 existing — owedTotal 50 goes into existingBalanceTotal
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 50 }),
                        formFieldValues: {},
                    }],
                }),
                // p2 new — deposit 80 goes into depositTotal
                makePlayer({ playerId: 'p2' }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });
            (jobCtx.paymentOption as WritableSignal<string>).set('Deposit');

            // existingBalance=50, depositTotal=80 → 130
            expect(service.currentTotal()).toBe(130);
        });

        it('should never let currentTotal go below 0', () => {
            // Create a scenario with zero fees
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: null }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });

            // The fee defaults to 100 via getAmount, so currentTotal should be >= 0
            expect(service.currentTotal()).toBeGreaterThanOrEqual(0);
        });

        it('should return 0 when there are no line items', () => {
            fp._set([]);
            playerState.setSelectedTeams({});

            expect(service.totalAmount()).toBe(0);
            expect(service.depositTotal()).toBe(0);
            expect(service.currentTotal()).toBe(0);
        });
    });

    // ─── ARB Calculations ────────────────────────────────────────────

    describe('ARB calculations', () => {
        it('should default arbOccurrences to 10 when not set', () => {
            expect(service.arbOccurrences()).toBe(10);
        });

        it('should use configured arbOccurrences when set', () => {
            (jobCtx.adnArbBillingOccurences as WritableSignal<number | null>).set(6);
            expect(service.arbOccurrences()).toBe(6);
        });

        it('should divide totalAmount evenly across occurrences', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 500 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });
            (jobCtx.adnArbBillingOccurences as WritableSignal<number | null>).set(5);

            expect(service.arbPerOccurrence()).toBe(100);
        });

        it('should round arbPerOccurrence to 2 decimal places', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 100 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });
            (jobCtx.adnArbBillingOccurences as WritableSignal<number | null>).set(3);

            // 100 / 3 = 33.333... → 33.33
            expect(service.arbPerOccurrence()).toBe(33.33);
        });
    });

    // ─── Discount Reset ──────────────────────────────────────────────

    describe('resetDiscount', () => {
        it('should clear message and success flag', () => {
            service.resetDiscount();

            expect(service.discountMessage()).toBeNull();
            expect(service.discountAppliedOk()).toBe(false);
        });
    });

    // ─── Discount Code — currentTotal reflects financials ────────────

    describe('discount code — currentTotal reflects financials', () => {
        it('should reflect updated owedTotal after partial discount', () => {
            // Backend applied $100 discount: feeBase=595, feeDiscount=100, owedTotal=495
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 495, feeBase: 595, feeDiscount: 100, feeTotal: 495 }),
                        formFieldValues: {},
                    }],
                }),
            ]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.currentTotal()).toBe(495);
        });

        it('should show zero when discount zeroes owedTotal', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 0, feeBase: 595, feeDiscount: 595, feeTotal: 0 }),
                        formFieldValues: {},
                    }],
                }),
            ]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.currentTotal()).toBe(0);
        });

        it('should use team fee when no financials present (new player, pre-discount)', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            fp._set([makePlayer({ playerId: 'p1' })]);
            playerState.setSelectedTeams({ p1: 't1' });

            expect(service.currentTotal()).toBe(595);
        });

        it('should sum owedTotals across multiple players with financials', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', fee: 400 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 400, feeBase: 595, feeDiscount: 195, feeTotal: 400 }),
                        formFieldValues: {},
                    }],
                }),
                makePlayer({
                    playerId: 'p2',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r2',
                        active: true,
                        assignedTeamId: 't2',
                        financials: makeFinancials({ owedTotal: 200, feeBase: 400, feeDiscount: 200, feeTotal: 200 }),
                        formFieldValues: {},
                    }],
                }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });

            expect(service.currentTotal()).toBe(600);
        });

        it('should sum discounted financials and team fee for mixed players', () => {
            // p1 existing with post-discount owedTotal, p2 new with team fee
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            teamSvc._addTeam(makeTeam({ teamId: 't2', fee: 300 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 495, feeBase: 595, feeDiscount: 100, feeTotal: 495 }),
                        formFieldValues: {},
                    }],
                }),
                makePlayer({ playerId: 'p2' }),
            ]);
            playerState.setSelectedTeams({ p1: 't1', p2: 't2' });

            expect(service.currentTotal()).toBe(795);
        });

        it('should not change currentTotal when resetDiscount is called (discount is in financials)', () => {
            teamSvc._addTeam(makeTeam({ teamId: 't1', fee: 595 }));
            fp._set([
                makePlayer({
                    playerId: 'p1',
                    registered: true,
                    selected: false,
                    priorRegistrations: [{
                        registrationId: 'r1',
                        active: true,
                        assignedTeamId: 't1',
                        financials: makeFinancials({ owedTotal: 495, feeBase: 595, feeDiscount: 100, feeTotal: 495 }),
                        formFieldValues: {},
                    }],
                }),
            ]);
            playerState.setSelectedTeams({ p1: 't1' });

            const totalBefore = service.currentTotal();
            service.resetDiscount();

            expect(service.discountMessage()).toBeNull();
            expect(service.discountAppliedOk()).toBe(false);
            expect(service.currentTotal()).toBe(totalBefore);
        });
    });
});
