import { TestBed } from '@angular/core/testing';
import { EligibilityService } from './eligibility.service';
import { PlayerStateService } from '../services/player-state.service';
import type { PlayerProfileFieldSchema, FamilyPlayerDto } from '../types/player-wizard.types';

// ── Helpers ──────────────────────────────────────────────────────────────

function makeField(overrides: Partial<PlayerProfileFieldSchema> = {}): PlayerProfileFieldSchema {
    return {
        name: 'someField',
        label: 'Some Field',
        type: 'text',
        required: false,
        options: [],
        placeholder: null,
        helpText: null,
        remoteUrl: null,
        errorMessage: null,
        ...overrides,
    };
}

function makePlayer(overrides: Partial<FamilyPlayerDto> = {}): FamilyPlayerDto {
    return {
        playerId: 'p1',
        firstName: 'Test',
        lastName: 'Player',
        gender: 'M',
        dob: null,
        registered: false,
        selected: true,
        priorRegistrations: [],
        defaultFieldValues: null,
        ...overrides,
    };
}

// ── Test suite ───────────────────────────────────────────────────────────

describe('EligibilityService', () => {
    let service: EligibilityService;
    let playerState: PlayerStateService;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [EligibilityService, PlayerStateService],
        });
        service = TestBed.inject(EligibilityService);
        playerState = TestBed.inject(PlayerStateService);
    });

    afterEach(() => {
        service.reset();
    });

    // ── determineEligibilityField ────────────────────────────────────

    describe('determineEligibilityField', () => {
        it('finds field matching BYGRADYEAR by name containing grad and year', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            const schemas = [
                makeField({ name: 'graduationYear', label: 'Graduation Year' }),
                makeField({ name: 'otherField', label: 'Other' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBe('graduationYear');
        });

        it('finds field matching BYAGEGROUP by label containing age and group', () => {
            service.setTeamConstraintType('BYAGEGROUP');
            const schemas = [
                makeField({ name: 'ag', label: 'Age Group Selection' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBe('ag');
        });

        it('finds field matching BYAGERANGE by name containing age and range', () => {
            service.setTeamConstraintType('BYAGERANGE');
            const schemas = [
                makeField({ name: 'playerAgeRange', label: 'Player Age Range' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBe('playerAgeRange');
        });

        it('finds field matching BYCLUBNAME by name containing club', () => {
            service.setTeamConstraintType('BYCLUBNAME');
            const schemas = [
                makeField({ name: 'clubName', label: 'Club Name' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBe('clubName');
        });

        it('returns null when no constraint type is set', () => {
            const schemas = [
                makeField({ name: 'graduationYear', label: 'Graduation Year' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBeNull();
        });

        it('returns null when no matching field exists in schemas', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            const schemas = [
                makeField({ name: 'firstName', label: 'First Name' }),
                makeField({ name: 'lastName', label: 'Last Name' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBeNull();
        });

        it('skips hidden and adminOnly fields', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            const schemas = [
                makeField({ name: 'graduationYear', label: 'Graduation Year', visibility: 'hidden' }),
                makeField({ name: 'gradYear', label: 'Grad Year', visibility: 'adminOnly' }),
                makeField({ name: 'publicGradYear', label: 'Public Grad Year', visibility: 'public' }),
            ];
            expect(service.determineEligibilityField(schemas)).toBe('publicGradYear');
        });
    });

    // ── updateUnifiedConstraintValue ─────────────────────────────────

    describe('updateUnifiedConstraintValue', () => {
        it('sets constraint value when all players share the same eligibility value', () => {
            playerState.setEligibilityForPlayer('p1', '2028');
            playerState.setEligibilityForPlayer('p2', '2028');

            service.updateUnifiedConstraintValue(['p1', 'p2']);

            expect(service.teamConstraintValue()).toBe('2028');
        });

        it('does not set constraint value when players have different values', () => {
            playerState.setEligibilityForPlayer('p1', '2028');
            playerState.setEligibilityForPlayer('p2', '2029');

            service.updateUnifiedConstraintValue(['p1', 'p2']);

            expect(service.teamConstraintValue()).toBeNull();
        });

        it('does not set constraint value for empty player list', () => {
            service.updateUnifiedConstraintValue([]);

            expect(service.teamConstraintValue()).toBeNull();
        });
    });

    // ── seedEligibilityFromSchemas ───────────────────────────────────

    describe('seedEligibilityFromSchemas', () => {
        it('seeds eligibility from form values when not already set', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            const schemas = [makeField({ name: 'graduationYear', label: 'Graduation Year' })];
            const players = [
                makePlayer({ playerId: 'p1', selected: true }),
                makePlayer({ playerId: 'p2', selected: true }),
            ];
            const getFormValue = (pid: string, _field: string) =>
                pid === 'p1' ? '2028' : '2028';

            service.seedEligibilityFromSchemas(schemas, players, ['p1', 'p2'], getFormValue);

            expect(playerState.getEligibilityForPlayer('p1')).toBe('2028');
            expect(playerState.getEligibilityForPlayer('p2')).toBe('2028');
        });

        it('does not overwrite existing eligibility', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            playerState.setEligibilityForPlayer('p1', '2027');

            const schemas = [makeField({ name: 'graduationYear', label: 'Graduation Year' })];
            const players = [makePlayer({ playerId: 'p1', selected: true })];
            const getFormValue = () => '2028';

            service.seedEligibilityFromSchemas(schemas, players, ['p1'], getFormValue);

            expect(playerState.getEligibilityForPlayer('p1')).toBe('2027');
        });

        it('skips players that are not selected and not registered', () => {
            service.setTeamConstraintType('BYGRADYEAR');
            const schemas = [makeField({ name: 'graduationYear', label: 'Graduation Year' })];
            const players = [
                makePlayer({ playerId: 'p1', selected: false, registered: false }),
            ];
            const getFormValue = () => '2028';

            service.seedEligibilityFromSchemas(schemas, players, ['p1'], getFormValue);

            expect(playerState.getEligibilityForPlayer('p1')).toBeUndefined();
        });
    });

    // ── pruneDeselectedTeams ─────────────────────────────────────────

    describe('pruneDeselectedTeams', () => {
        it('removes teams for players not in selectedIds', () => {
            playerState.setSelectedTeams({
                p1: 'team-a',
                p2: 'team-b',
                p3: 'team-c',
            });

            service.pruneDeselectedTeams(new Set(['p1']));

            const teams = playerState.selectedTeams();
            expect(teams['p1']).toBe('team-a');
            expect(teams['p2']).toBeUndefined();
            expect(teams['p3']).toBeUndefined();
        });

        it('keeps all teams when all players are in selectedIds', () => {
            playerState.setSelectedTeams({
                p1: 'team-a',
                p2: 'team-b',
            });

            service.pruneDeselectedTeams(new Set(['p1', 'p2']));

            const teams = playerState.selectedTeams();
            expect(teams['p1']).toBe('team-a');
            expect(teams['p2']).toBe('team-b');
        });
    });
});
