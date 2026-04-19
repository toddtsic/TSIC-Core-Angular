import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { PlayerFormsService } from './player-forms.service';
import { FormSchemaService } from '@views/registration/player/services/form-schema.service';
import type { PlayerProfileFieldSchema } from '../types/player-wizard.types';

// ── Helpers ──────────────────────────────────────────────────────────

function mkField(overrides: Partial<PlayerProfileFieldSchema> = {}): PlayerProfileFieldSchema {
    return {
        name: 'field1',
        label: 'Field 1',
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

const alwaysUnlocked = (_pid: string) => false;
const alwaysLocked = (_pid: string) => true;
const alwaysVisible = (_pid: string, _f: PlayerProfileFieldSchema) => true;
const neverVisible = (_pid: string, _f: PlayerProfileFieldSchema) => false;

// ── Stub FormSchemaService ───────────────────────────────────────────

class StubFormSchemaService {
    aliasFieldMap = signal<Record<string, string>>({});
    profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
}

// ── Tests ────────────────────────────────────────────────────────────

describe('PlayerFormsService', () => {
    let service: PlayerFormsService;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                PlayerFormsService,
                { provide: FormSchemaService, useClass: StubFormSchemaService },
            ],
        });
        service = TestBed.inject(PlayerFormsService);
    });

    // ── 1. Field Value Management ────────────────────────────────────

    describe('Field Value Management', () => {
        it('setPlayerFieldValue stores and getPlayerFieldValue retrieves', () => {
            service.setPlayerFieldValue('p1', 'firstName', 'Alice');
            expect(service.getPlayerFieldValue('p1', 'firstName')).toBe('Alice');
        });

        it('setting sportassnid to 424242424242 marks US Lax status as valid', () => {
            service.setPlayerFieldValue('p1', 'sportassnid', '424242424242');
            const status = service.usLaxStatus();
            expect(status['p1'].status).toBe('valid');
        });

        it('setting sportassnid to other value marks US Lax status as idle', () => {
            // First set to valid
            service.setPlayerFieldValue('p1', 'sportassnid', '424242424242');
            expect(service.usLaxStatus()['p1'].status).toBe('valid');

            // Now set to something else
            service.setPlayerFieldValue('p1', 'sportassnid', '999999999999');
            expect(service.usLaxStatus()['p1'].status).toBe('idle');
        });

        it('pruneDeselectedPlayers removes form values for deselected players', () => {
            service.setPlayerFieldValue('p1', 'name', 'Alice');
            service.setPlayerFieldValue('p2', 'name', 'Bob');
            service.setPlayerFieldValue('p3', 'name', 'Carol');

            service.pruneDeselectedPlayers(new Set(['p1', 'p3']));

            expect(service.getPlayerFieldValue('p1', 'name')).toBe('Alice');
            expect(service.getPlayerFieldValue('p2', 'name')).toBeUndefined();
            expect(service.getPlayerFieldValue('p3', 'name')).toBe('Carol');
        });
    });

    // ── 2. Validation ────────────────────────────────────────────────

    describe('Validation', () => {
        it('required text field empty returns Required', () => {
            const field = mkField({ name: 'first', required: true, type: 'text' });
            service.setPlayerFieldValue('p1', 'first', '');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Required');
        });

        it('required text field filled returns null', () => {
            const field = mkField({ name: 'first', required: true, type: 'text' });
            service.setPlayerFieldValue('p1', 'first', 'Alice');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBeNull();
        });

        it('required checkbox unchecked returns Required', () => {
            const field = mkField({ name: 'agree', required: true, type: 'checkbox' });
            service.setPlayerFieldValue('p1', 'agree', false);
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Required');
        });

        it('required checkbox checked returns null', () => {
            const field = mkField({ name: 'agree', required: true, type: 'checkbox' });
            service.setPlayerFieldValue('p1', 'agree', true);
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBeNull();
        });

        it('required multiselect empty returns Required', () => {
            const field = mkField({ name: 'positions', required: true, type: 'multiselect', options: ['A', 'B'] });
            service.setPlayerFieldValue('p1', 'positions', []);
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Required');
        });

        it('required multiselect with values returns null', () => {
            const field = mkField({ name: 'positions', required: true, type: 'multiselect', options: ['A', 'B'] });
            service.setPlayerFieldValue('p1', 'positions', ['A']);
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBeNull();
        });

        it('number field with non-number returns Must be a number', () => {
            const field = mkField({ name: 'jersey', type: 'number' });
            service.setPlayerFieldValue('p1', 'jersey', 'abc');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Must be a number');
        });

        it('date field with invalid date returns Invalid date', () => {
            const field = mkField({ name: 'dob', type: 'date' });
            service.setPlayerFieldValue('p1', 'dob', 'not-a-date');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Invalid date');
        });

        it('select field with invalid option returns Required when required', () => {
            const field = mkField({ name: 'size', type: 'select', required: true, options: ['S', 'M', 'L'] });
            service.setPlayerFieldValue('p1', 'size', 'XXL');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Required');
        });

        it('select field with invalid option returns null when not required', () => {
            const field = mkField({ name: 'size', type: 'select', required: false, options: ['S', 'M', 'L'] });
            service.setPlayerFieldValue('p1', 'size', 'XXL');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBeNull();
        });

        it('email field with invalid email returns Invalid email address', () => {
            const field = mkField({ name: 'email', label: 'Email', type: 'text' });
            service.setPlayerFieldValue('p1', 'email', 'not-an-email');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBe('Invalid email address');
        });

        it('email field with valid email returns null', () => {
            const field = mkField({ name: 'email', label: 'Email', type: 'text' });
            service.setPlayerFieldValue('p1', 'email', 'test@example.com');
            expect(service.getFieldError('p1', field, alwaysUnlocked, alwaysVisible)).toBeNull();
        });

        it('locked player always returns null (valid)', () => {
            const field = mkField({ name: 'first', required: true, type: 'text' });
            // Don't set any value — locked player should still be valid
            expect(service.getFieldError('p1', field, alwaysLocked, alwaysVisible)).toBeNull();
        });

        it('hidden field always returns null (valid)', () => {
            const field = mkField({ name: 'first', required: true, type: 'text' });
            // Don't set any value — hidden field should still be valid
            expect(service.getFieldError('p1', field, alwaysUnlocked, neverVisible)).toBeNull();
        });

        it('areFormsValid returns true when all fields valid', () => {
            const schemas = [
                mkField({ name: 'first', required: true, type: 'text' }),
                mkField({ name: 'last', required: true, type: 'text' }),
            ];
            service.setPlayerFieldValue('p1', 'first', 'Alice');
            service.setPlayerFieldValue('p1', 'last', 'Smith');

            expect(service.areFormsValid(schemas, ['p1'], alwaysUnlocked, alwaysVisible)).toBe(true);
        });

        it('areFormsValid returns false when any field invalid', () => {
            const schemas = [
                mkField({ name: 'first', required: true, type: 'text' }),
                mkField({ name: 'last', required: true, type: 'text' }),
            ];
            service.setPlayerFieldValue('p1', 'first', 'Alice');
            // 'last' is not set → Required

            expect(service.areFormsValid(schemas, ['p1'], alwaysUnlocked, alwaysVisible)).toBe(false);
        });

        it('areFormsValid skips locked players', () => {
            const schemas = [mkField({ name: 'first', required: true, type: 'text' })];
            // p1 has no value but is locked → should be treated as valid
            expect(service.areFormsValid(schemas, ['p1'], alwaysLocked, alwaysVisible)).toBe(true);
        });
    });

    // ── 3. Field Visibility ──────────────────────────────────────────

    describe('Field Visibility', () => {
        it('hidden visibility is not visible', () => {
            const field = mkField({ name: 'secret', visibility: 'hidden' });
            expect(service.isFieldVisibleForPlayer('p1', field, [], null)).toBe(false);
        });

        it('adminOnly visibility is not visible', () => {
            const field = mkField({ name: 'adminNote', visibility: 'adminOnly' });
            expect(service.isFieldVisibleForPlayer('p1', field, [], null)).toBe(false);
        });

        it('team/teamid fields are not visible', () => {
            const teamField = mkField({ name: 'team', label: 'Team' });
            const teamIdField = mkField({ name: 'teamid', label: 'Team ID' });
            const teamsField = mkField({ name: 'teams', label: 'Teams' });

            expect(service.isFieldVisibleForPlayer('p1', teamField, [], null)).toBe(false);
            expect(service.isFieldVisibleForPlayer('p1', teamIdField, [], null)).toBe(false);
            expect(service.isFieldVisibleForPlayer('p1', teamsField, [], null)).toBe(false);
        });

        it('waiver fields are not visible when in waiverFieldNames', () => {
            const field = mkField({ name: 'waiverAccepted', label: 'Waiver' });
            expect(service.isFieldVisibleForPlayer('p1', field, ['waiverAccepted'], null)).toBe(false);
        });

        it('BYGRADYEAR constraint hides grad year fields', () => {
            const gradField = mkField({ name: 'GradYear', label: 'Graduation Year' });
            expect(service.isFieldVisibleForPlayer('p1', gradField, [], 'BYGRADYEAR')).toBe(false);
        });

        it('BYAGEGROUP constraint hides age group fields', () => {
            const agField = mkField({ name: 'AgeGroup', label: 'Age Group' });
            expect(service.isFieldVisibleForPlayer('p1', agField, [], 'BYAGEGROUP')).toBe(false);
        });

        it('conditional field visible when condition met', () => {
            const field = mkField({
                name: 'allergies',
                label: 'Allergies',
                condition: { field: 'hasAllergies', value: 'yes', operator: 'equals' },
            });
            service.setPlayerFieldValue('p1', 'hasAllergies', 'yes');
            expect(service.isFieldVisibleForPlayer('p1', field, [], null)).toBe(true);
        });

        it('conditional field hidden when condition not met', () => {
            const field = mkField({
                name: 'allergies',
                label: 'Allergies',
                condition: { field: 'hasAllergies', value: 'yes', operator: 'equals' },
            });
            service.setPlayerFieldValue('p1', 'hasAllergies', 'no');
            expect(service.isFieldVisibleForPlayer('p1', field, [], null)).toBe(false);
        });

        // ── Recruiting field gating (SP-040) ─────────────────────────
        // Tournament path: gated by jsonOptions.List_RecruitingGradYears (NCAA contact rules).
        // Non-tournament path: always shown — clubs may use these on profile.
        it('tournament: recruiting field hidden when recruitingGradYears is empty', () => {
            const gpa = mkField({ name: 'gpa', label: 'GPA' });
            expect(service.isFieldVisibleForPlayer('p1', gpa, [], null, true, [], '2026')).toBe(false);
        });

        it('tournament: recruiting field hidden when player grad year not in list', () => {
            const gpa = mkField({ name: 'gpa', label: 'GPA' });
            expect(service.isFieldVisibleForPlayer('p1', gpa, [], null, true, ['2024', '2025'], '2030')).toBe(false);
        });

        it('tournament: recruiting field visible when player grad year matches list', () => {
            const gpa = mkField({ name: 'gpa', label: 'GPA' });
            expect(service.isFieldVisibleForPlayer('p1', gpa, [], null, true, ['2024', '2025', '2026'], '2025')).toBe(true);
        });

        it('tournament: recruiting field hidden when player grad year is null', () => {
            const sat = mkField({ name: 'satMath', label: 'SAT Math' });
            expect(service.isFieldVisibleForPlayer('p1', sat, [], null, true, ['2024'], null)).toBe(false);
        });

        it('non-tournament: recruiting field always visible regardless of grad year list', () => {
            const gpa = mkField({ name: 'gpa', label: 'GPA' });
            // No recruiting list, no grad year, but isTournament=false → still shown
            expect(service.isFieldVisibleForPlayer('p1', gpa, [], null, false, [], null)).toBe(true);
        });

        it('non-recruiting field unaffected by recruiting gating', () => {
            const school = mkField({ name: 'schoolName', label: 'School' });
            expect(service.isFieldVisibleForPlayer('p1', school, [], null, true, [], null)).toBe(true);
        });
    });

    // ── 4. Form Seeding ──────────────────────────────────────────────

    describe('Form Seeding', () => {
        it('initializeFormValuesForSelectedPlayers creates null entries for all schema fields', () => {
            const schemas = [
                mkField({ name: 'first' }),
                mkField({ name: 'last' }),
            ];
            service.initializeFormValuesForSelectedPlayers(schemas, ['p1', 'p2']);

            expect(service.getPlayerFieldValue('p1', 'first')).toBeNull();
            expect(service.getPlayerFieldValue('p1', 'last')).toBeNull();
            expect(service.getPlayerFieldValue('p2', 'first')).toBeNull();
            expect(service.getPlayerFieldValue('p2', 'last')).toBeNull();
        });

        it('seedFromDefaults applies default values for unregistered players', () => {
            const schemas = [
                mkField({ name: 'FirstName' }),
                mkField({ name: 'LastName' }),
            ];

            // Initialize form values first
            service.initializeFormValuesForSelectedPlayers(schemas, ['p1']);

            const players = [
                {
                    playerId: 'p1',
                    registered: false,
                    selected: true,
                    defaultFieldValues: { firstname: 'Alice', lastname: 'Smith' },
                },
            ] as any[];

            service.seedFromDefaults(schemas, players);

            expect(service.getPlayerFieldValue('p1', 'FirstName')).toBe('Alice');
            expect(service.getPlayerFieldValue('p1', 'LastName')).toBe('Smith');
        });

        it('clearInvalidSelectValues removes options not in the options list', () => {
            const schemas = [
                mkField({ name: 'size', type: 'select', options: ['S', 'M', 'L'] }),
            ];
            service.setPlayerFieldValue('p1', 'size', 'XXL');

            service.clearInvalidSelectValues(schemas);

            expect(service.getPlayerFieldValue('p1', 'size')).toBe('');
        });

        it('clearInvalidSelectValues normalizes case of valid options', () => {
            const schemas = [
                mkField({ name: 'size', type: 'select', options: ['Small', 'Medium', 'Large'] }),
            ];
            service.setPlayerFieldValue('p1', 'size', 'small');

            service.clearInvalidSelectValues(schemas);

            // Should normalize to the canonical case from options
            expect(service.getPlayerFieldValue('p1', 'size')).toBe('Small');
        });
    });
});
