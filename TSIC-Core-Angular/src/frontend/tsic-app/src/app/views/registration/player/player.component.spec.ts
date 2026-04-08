import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { PlayerWizardV2Component } from './player.component';
import { PlayerWizardStateService } from './state/player-wizard-state.service';
import { PaymentV2Service } from './state/payment-v2.service';
import { TeamService } from './services/team.service';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { ReserveTeamsResponseDto, PreSubmitPlayerRegistrationResponseDto } from '@core/api';

/**
 * PLAYER WIZARD NAVIGATION TESTS
 *
 * Tests the next() method's decision logic — the code that determines
 * what the registrant sees when they click Continue at each wizard step.
 *
 * Key behaviors tested:
 *   - Teams step: full team → warning toast, stay on step
 *   - Teams step: waitlisted → warning toast with waitlist name, advance
 *   - Teams step: room available → advance silently
 *   - Teams step: HTTP error → danger toast, stay on step
 *   - Review step: success → advance to payment
 *   - Review step: failure → danger toast, stay on step
 *   - Non-gated step: advance without any API call
 *
 * All services are stubbed. No HTTP calls, no template rendering.
 */
describe('PlayerWizardV2Component — next() navigation', () => {
    let component: PlayerWizardV2Component;
    let toastShowFn: ReturnType<typeof vi.fn>;
    let reserveTeamsFn: ReturnType<typeof vi.fn>;
    let preSubmitFn: ReturnType<typeof vi.fn>;

    // Stub sub-services that feed computed signals in the component
    const eligibilityStub = {
        teamConstraintType: signal<string | null>(null),
        selectedTeams: signal<Record<string, string>>({}),
        getEligibilityForPlayer: () => null,
    };

    const familyPlayersStub = {
        familyUser: signal<{ familyUserId: string; userName: string } | null>(null),
        selectedPlayerIds: signal<string[]>([]),
        isPlayerLocked: () => false,
    };

    const jobCtxStub = {
        isCacMode: signal(false),
        waiverDefinitions: signal<unknown[]>([]),
        profileFieldSchemas: signal<unknown[]>([]),
        waiverFieldNames: signal<string[]>([]),
        allRequiredWaiversAccepted: signal(false),
        jobPath: signal('test-job'),
        resolveApiBase: () => 'http://localhost',
    };

    const playerFormsStub = {
        areFormsValid: () => true,
        isFieldVisibleForPlayer: () => true,
    };

    beforeEach(() => {
        // Create fresh spies each test
        toastShowFn = vi.fn();
        reserveTeamsFn = vi.fn();
        preSubmitFn = vi.fn();

        const stateStub = {
            eligibility: eligibilityStub,
            familyPlayers: familyPlayersStub,
            jobCtx: jobCtxStub,
            playerForms: playerFormsStub,
            confirmation: signal(null),
            reset: vi.fn(),
            initialize: vi.fn(),
            reserveTeams: reserveTeamsFn,
            preSubmitRegistration: preSubmitFn,
        };

        TestBed.configureTestingModule({
            imports: [PlayerWizardV2Component],
            providers: [
                { provide: PlayerWizardStateService, useValue: stateStub },
                { provide: ToastService, useValue: { show: toastShowFn } },
                { provide: PaymentV2Service, useValue: { currentTotal: signal(0) } },
                { provide: TeamService, useValue: { loadForJob: vi.fn() } },
                { provide: AuthService, useValue: { currentUser: signal(null), logoutLocal: vi.fn() } },
                { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
                {
                    provide: ActivatedRoute,
                    useValue: {
                        snapshot: {
                            paramMap: { get: () => 'test-job' },
                            queryParamMap: { get: () => null },
                            parent: null,
                        },
                    },
                },
            ],
            schemas: [NO_ERRORS_SCHEMA],
        });

        const fixture = TestBed.createComponent(PlayerWizardV2Component);
        component = fixture.componentInstance;
    });

    /** Position the wizard on a specific step by ID. */
    function goToStep(stepId: string): void {
        const idx = component.activeSteps().findIndex(s => s.id === stepId);
        if (idx < 0) throw new Error(`Step '${stepId}' not found in activeSteps`);
        (component as any)._currentIndex.set(idx);
    }

    // ── Teams step tests ─────────────────────────────────────────────

    it('teams step: room available → advances to next step', async () => {
        goToStep('teams');
        const before = component.currentIndex();

        reserveTeamsFn.mockResolvedValue({
            teamResults: [{ playerId: 'p1', teamId: 't1', isFull: false, teamName: 'Storm U12', message: 'ok', registrationCreated: true, isWaitlisted: false }],
            hasFullTeams: false,
        } satisfies ReserveTeamsResponseDto);

        await component.next();

        expect(component.currentIndex()).toBe(before + 1);
        expect(toastShowFn).not.toHaveBeenCalled();
    });

    it('teams step: team full → warning toast, stays on step', async () => {
        goToStep('teams');
        const before = component.currentIndex();

        reserveTeamsFn.mockResolvedValue({
            teamResults: [{ playerId: 'p1', teamId: 't1', isFull: true, teamName: 'Storm U12', message: 'full', registrationCreated: false }],
            hasFullTeams: true,
        } satisfies ReserveTeamsResponseDto);

        await component.next();

        expect(component.currentIndex()).toBe(before);
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('Storm U12'),
            'warning',
            expect.any(Number),
        );
    });

    it('teams step: waitlisted → warning toast with waitlist name, advances', async () => {
        goToStep('teams');
        const before = component.currentIndex();

        reserveTeamsFn.mockResolvedValue({
            teamResults: [{
                playerId: 'p1', teamId: 'wt1', isFull: false,
                teamName: 'WAITLIST - Storm U12', message: 'ok', registrationCreated: true,
                isWaitlisted: true, waitlistTeamName: 'WAITLIST - Storm U12',
            }],
            hasFullTeams: false,
        } satisfies ReserveTeamsResponseDto);

        await component.next();

        expect(component.currentIndex()).toBe(before + 1);
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('waitlist'),
            'warning',
            expect.any(Number),
        );
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('WAITLIST - Storm U12'),
            expect.anything(),
            expect.anything(),
        );
    });

    it('teams step: HTTP error → danger toast, stays on step', async () => {
        goToStep('teams');
        const before = component.currentIndex();

        reserveTeamsFn.mockRejectedValue(new Error('Network error'));

        await component.next();

        expect(component.currentIndex()).toBe(before);
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('Could not reserve'),
            'danger',
            expect.any(Number),
        );
    });

    // ── Review step tests ────────────────────────────────────────────

    it('review step: success → advances to payment', async () => {
        goToStep('review');
        const before = component.currentIndex();

        preSubmitFn.mockResolvedValue({
            teamResults: [], nextTab: 'Payment', insurance: null, validationErrors: null,
        });

        await component.next();

        expect(component.currentIndex()).toBe(before + 1);
        expect(toastShowFn).not.toHaveBeenCalled();
    });

    it('review step: failure → danger toast, stays on step', async () => {
        goToStep('review');
        const before = component.currentIndex();

        preSubmitFn.mockRejectedValue(new Error('Validation failed'));

        await component.next();

        expect(component.currentIndex()).toBe(before);
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('Registration submission failed'),
            'danger',
            expect.any(Number),
        );
    });

    // ── Non-gated step test ──────────────────────────────────────────

    it('non-gated step (forms): advances without API call', async () => {
        goToStep('forms');
        const before = component.currentIndex();

        await component.next();

        expect(component.currentIndex()).toBe(before + 1);
        expect(reserveTeamsFn).not.toHaveBeenCalled();
        expect(preSubmitFn).not.toHaveBeenCalled();
        expect(toastShowFn).not.toHaveBeenCalled();
    });
});
