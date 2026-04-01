import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TeamService, AvailableTeam } from './team.service';
import { JobContextService } from '../state/job-context.service';
import { EligibilityService } from '../state/eligibility.service';
import { environment } from '@environments/environment';

/** Helper to create an AvailableTeam with sensible defaults. */
function mkTeam(overrides: Partial<AvailableTeam>): AvailableTeam {
    return {
        teamId: overrides.teamId ?? crypto.randomUUID(),
        teamName: overrides.teamName ?? 'Test Team',
        agegroupId: overrides.agegroupId ?? 'ag-1',
        agegroupName: overrides.agegroupName ?? 'U12',
        divisionId: overrides.divisionId ?? null,
        divisionName: overrides.divisionName ?? null,
        maxRosterSize: overrides.maxRosterSize ?? 20,
        currentRosterSize: overrides.currentRosterSize ?? 10,
        rosterIsFull: overrides.rosterIsFull ?? false,
        fee: overrides.fee ?? null,
        deposit: overrides.deposit ?? null,
        jobUsesWaitlists: overrides.jobUsesWaitlists ?? false,
        ...(overrides.teamAllowsSelfRostering !== undefined && { teamAllowsSelfRostering: overrides.teamAllowsSelfRostering }),
        ...(overrides.agegroupAllowsSelfRostering !== undefined && { agegroupAllowsSelfRostering: overrides.agegroupAllowsSelfRostering }),
        ...(overrides.waitlistTeamId !== undefined && { waitlistTeamId: overrides.waitlistTeamId }),
    };
}

describe('TeamService', () => {
    let service: TeamService;
    let httpCtrl: HttpTestingController;

    // Writable signals that tests can control
    const jobPathSignal = signal<string>('test-job');
    const constraintTypeSignal = signal<string | null>(null);
    const constraintValueSignal = signal<string | null>(null);

    beforeEach(() => {
        // Reset signals before each test
        jobPathSignal.set('test-job');
        constraintTypeSignal.set(null);
        constraintValueSignal.set(null);

        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(),
                provideHttpClientTesting(),
                TeamService,
                {
                    provide: JobContextService,
                    useValue: { jobPath: jobPathSignal.asReadonly() },
                },
                {
                    provide: EligibilityService,
                    useValue: {
                        teamConstraintType: constraintTypeSignal.asReadonly(),
                        teamConstraintValue: constraintValueSignal.asReadonly(),
                    },
                },
            ],
        });

        service = TestBed.inject(TeamService);
        httpCtrl = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
        httpCtrl.verify();
    });

    /** Triggers loadForJob and flushes the HTTP request with the given teams. */
    function loadTeams(teams: AvailableTeam[]): void {
        service.loadForJob('test-job');
        const req = httpCtrl.expectOne(
            `${environment.apiUrl}/jobs/test-job/available-teams`
        );
        req.flush(teams);
    }

    // ── No constraint ────────────────────────────────────────────────

    describe('no constraint', () => {
        it('should return all teams when no constraint is set', () => {
            const teams = [
                mkTeam({ teamName: 'Alpha' }),
                mkTeam({ teamName: 'Bravo' }),
                mkTeam({ teamName: 'Charlie' }),
            ];

            loadTeams(teams);

            expect(service.filteredTeams()).toHaveLength(3);
        });
    });

    // ── BYGRADYEAR ───────────────────────────────────────────────────

    describe('BYGRADYEAR', () => {
        const teams = [
            mkTeam({ teamName: 'Eagles 2028', agegroupName: 'Class of 2028' }),
            mkTeam({ teamName: 'Hawks 2029', agegroupName: 'Class of 2029' }),
            mkTeam({ teamName: 'Ravens', agegroupName: 'Open' }),
        ];

        it('should filter teams containing grad year in name or agegroup', () => {
            constraintTypeSignal.set('BYGRADYEAR');
            constraintValueSignal.set('2028');

            loadTeams(teams);

            const result = service.filteredTeams();
            expect(result).toHaveLength(1);
            expect(result[0].teamName).toBe('Eagles 2028');
        });

        it('should exclude non-matching teams', () => {
            constraintTypeSignal.set('BYGRADYEAR');
            constraintValueSignal.set('2030');

            loadTeams(teams);

            expect(service.filteredTeams()).toHaveLength(0);
        });
    });

    // ── BYAGEGROUP ───────────────────────────────────────────────────

    describe('BYAGEGROUP', () => {
        const teams = [
            mkTeam({ teamName: 'Team A', agegroupName: 'U12' }),
            mkTeam({ teamName: 'Team B', agegroupName: 'U12' }),
            mkTeam({ teamName: 'Team C', agegroupName: 'U14' }),
        ];

        it('should filter teams by exact agegroup match (case-insensitive)', () => {
            constraintTypeSignal.set('BYAGEGROUP');
            constraintValueSignal.set('u12');

            loadTeams(teams);

            const result = service.filteredTeams();
            expect(result).toHaveLength(2);
            expect(result.every(t => t.agegroupName === 'U12')).toBe(true);
        });

        it('should exclude non-matching agegroups', () => {
            constraintTypeSignal.set('BYAGEGROUP');
            constraintValueSignal.set('U16');

            loadTeams(teams);

            expect(service.filteredTeams()).toHaveLength(0);
        });
    });

    // ── BYAGERANGE ───────────────────────────────────────────────────

    describe('BYAGERANGE', () => {
        it('should filter teams by substring match in teamName', () => {
            const teams = [
                mkTeam({ teamName: '10-12 Boys Blue' }),
                mkTeam({ teamName: '10-12 Girls Red' }),
                mkTeam({ teamName: '13-14 Boys Green' }),
            ];

            constraintTypeSignal.set('BYAGERANGE');
            constraintValueSignal.set('10-12');

            loadTeams(teams);

            const result = service.filteredTeams();
            expect(result).toHaveLength(2);
            expect(result.every(t => t.teamName.includes('10-12'))).toBe(true);
        });
    });

    // ── BYCLUBNAME ───────────────────────────────────────────────────

    describe('BYCLUBNAME', () => {
        it('should filter teams by substring match in teamName', () => {
            const teams = [
                mkTeam({ teamName: 'Northside Lightning U12' }),
                mkTeam({ teamName: 'Southside Thunder U12' }),
                mkTeam({ teamName: 'Northside Lightning U14' }),
            ];

            constraintTypeSignal.set('BYCLUBNAME');
            constraintValueSignal.set('Northside');

            loadTeams(teams);

            const result = service.filteredTeams();
            expect(result).toHaveLength(2);
            expect(result.every(t => t.teamName.includes('Northside'))).toBe(true);
        });
    });
});
