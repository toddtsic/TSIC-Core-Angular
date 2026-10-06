import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { TeamTeamsStepComponent } from './teams-step.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { ClubService } from '@infrastructure/services/club.service';
import type { RegisterTeamResponse, ClubTeamDto } from '@core/api';

/**
 * TEAM WIZARD — REGISTER RESPONSE TESTS
 *
 * Tests the subscribe handler in teams-step that processes the
 * registerTeamForEvent response. This is the code that determines
 * what the club rep sees after clicking an age group to register a team.
 *
 * Key behaviors tested:
 *   - Success → green toast with team and event name
 *   - Success + waitlisted → warning toast with the age group (WAITLIST prefix stripped)
 *   - Refusal (HTTP 4xx, e.g. quota exceeded) → no component toast; the global
 *     interceptor already shows the server's reason. The data reloads.
 */
describe('TeamTeamsStepComponent — register response handling', () => {
    let component: TeamTeamsStepComponent;
    let toastShowFn: ReturnType<typeof vi.fn>;
    let registerFn: ReturnType<typeof vi.fn>;

    const testTeam: ClubTeamDto = {
        clubTeamId: 42,
        clubTeamName: 'Storm U12',
        clubTeamGradYear: '2030',
        clubTeamLevelOfPlay: 'A',
        bHasBeenScheduled: false,
        bHasEventRegistrations: false,
        bArchived: false,
    };

    beforeEach(() => {
        toastShowFn = vi.fn();
        registerFn = vi.fn();

        TestBed.configureTestingModule({
            imports: [TeamTeamsStepComponent],
            providers: [
                {
                    provide: TeamRegistrationService,
                    useValue: {
                        registerTeamForEvent: registerFn,
                        getTeamsMetadata: vi.fn().mockReturnValue(of({})),
                        unregisterTeamFromEvent: vi.fn().mockReturnValue(of(void 0)),
                    },
                },
                { provide: ToastService, useValue: { show: toastShowFn } },
                {
                    provide: TeamWizardStateService,
                    useValue: {
                        clubRepRegistration: signal(null),
                        jobPath: signal('test-job'),
                        setHasActiveDiscountCodes: vi.fn(),
                        applyTeamsMetadata: vi.fn(),
                        teamPayment: {
                            setTeams: vi.fn(),
                            setJobPath: vi.fn(),
                            setPaymentConfig: vi.fn(),
                        },
                    },
                },
                {
                    provide: JobService,
                    useValue: { currentJob: signal({ jobName: 'Test Tournament' }) },
                },
                { provide: ClubService, useValue: { renameClub: vi.fn() } },
            ],
            schemas: [NO_ERRORS_SCHEMA],
        });

        const fixture = TestBed.createComponent(TeamTeamsStepComponent);
        component = fixture.componentInstance;
    });

    // ── Tests ─────────────────────────────────────────────────────────

    it('success → success toast naming the team and the event', () => {
        registerFn.mockReturnValue(of({
            success: true,
            teamId: 'new-team-id',
            isWaitlisted: false,
        } satisfies RegisterTeamResponse));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            'Storm U12 registered for Test Tournament.',
            'success',
            expect.any(Number),
        );
    });

    it('success + waitlisted → warning toast with the age group, WAITLIST prefix stripped', () => {
        registerFn.mockReturnValue(of({
            success: true,
            teamId: 'waitlist-team-id',
            isWaitlisted: true,
            waitlistAgegroupName: 'WAITLIST - Boys U14',
        } satisfies RegisterTeamResponse));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            'Storm U12 waitlisted for Boys U14',
            'warning',
            expect.any(Number),
        );
    });

    it('refusal (HTTP 4xx) → no second toast (the interceptor shows the reason); data reloads', () => {
        registerFn.mockReturnValue(throwError(() => new Error('Server error')));
        const getMetadataFn = TestBed.inject(TeamRegistrationService).getTeamsMetadata as ReturnType<typeof vi.fn>;
        getMetadataFn.mockClear();

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).not.toHaveBeenCalled();
        expect(getMetadataFn).toHaveBeenCalledTimes(1);
    });
});
