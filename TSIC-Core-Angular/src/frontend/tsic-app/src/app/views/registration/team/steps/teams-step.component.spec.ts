import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { TeamTeamsStepComponent } from './teams-step.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import type { RegisterTeamResponse, ClubTeamDto } from '@core/api';

/**
 * TEAM WIZARD — REGISTER RESPONSE TESTS
 *
 * Tests the subscribe handler in teams-step that processes the
 * registerTeamForEvent response. This is the code that determines
 * what the club rep sees after clicking an age group to register a team.
 *
 * Key behaviors tested:
 *   - Success → green toast with team name
 *   - Success + waitlisted → warning toast with waitlist agegroup name
 *   - Success: false (quota exceeded) → danger toast with server message
 *   - HTTP error → danger toast with generic message
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
            ],
            schemas: [NO_ERRORS_SCHEMA],
        });

        const fixture = TestBed.createComponent(TeamTeamsStepComponent);
        component = fixture.componentInstance;
    });

    // ── Tests ─────────────────────────────────────────────────────────

    it('success → success toast with team name', () => {
        registerFn.mockReturnValue(of({
            success: true,
            teamId: 'new-team-id',
            isWaitlisted: false,
        } satisfies RegisterTeamResponse));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('Storm U12 entered!'),
            'success',
            expect.any(Number),
        );
    });

    it('success + waitlisted → warning toast with waitlist agegroup name', () => {
        registerFn.mockReturnValue(of({
            success: true,
            teamId: 'waitlist-team-id',
            isWaitlisted: true,
            waitlistAgegroupName: 'WAITLIST - Boys U14',
        } satisfies RegisterTeamResponse));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('waitlisted'),
            'warning',
            expect.any(Number),
        );
        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('WAITLIST - Boys U14'),
            expect.anything(),
            expect.anything(),
        );
    });

    it('success: false (quota exceeded) → danger toast with server message', () => {
        registerFn.mockReturnValue(of({
            success: false,
            teamId: '',
            message: 'Your club has reached the maximum of 3 team(s) allowed in Boys U14.',
        } satisfies RegisterTeamResponse));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            expect.stringContaining('maximum of 3'),
            'danger',
            expect.any(Number),
        );
    });

    it('HTTP error → danger toast with generic message', () => {
        registerFn.mockReturnValue(throwError(() => new Error('Server error')));

        component.onSelectAgeGroup(testTeam, 'ag-1');

        expect(toastShowFn).toHaveBeenCalledWith(
            'Failed to register team.',
            'danger',
            expect.any(Number),
        );
    });
});
