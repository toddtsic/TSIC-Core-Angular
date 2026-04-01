import { TestBed } from '@angular/core/testing';
import { WaiverStateService } from './waiver-state.service';
import type { WaiverDefinition } from './registration-wizard.service';
import type { FamilyPlayerDto } from '@core/api';

// -- Helpers -----------------------------------------------------------------

function makeDef(overrides: Partial<WaiverDefinition> = {}): WaiverDefinition {
    return {
        id: 'PlayerRegReleaseOfLiability',
        title: 'Player Waiver',
        html: '<p>Waiver text</p>',
        required: true,
        version: '1',
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

function makeSchema(overrides: Partial<{ name: string; label: string; type: string; required: boolean; visibility?: string }> = {}) {
    return {
        name: 'someField',
        label: 'Some Field',
        type: 'text',
        required: false,
        ...overrides,
    };
}

// -- Test Suite --------------------------------------------------------------

describe('WaiverStateService', () => {
    let service: WaiverStateService;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [WaiverStateService],
        });
        service = TestBed.inject(WaiverStateService);
    });

    // ── 1. Waiver Acceptance ────────────────────────────────────────────

    describe('Waiver Acceptance', () => {
        it('setWaiverAccepted with ID sets both ID and field name in acceptance map', () => {
            const def = makeDef({ id: 'waiver1' });
            service.setDefinitions([def]);
            service.setBindings({ waiver1: 'chkWaiver' });

            service.setWaiverAccepted('waiver1', true);

            const accepted = service.waiversAccepted();
            expect(accepted['chkWaiver']).toBe(true);
            expect(accepted['waiver1']).toBe(true);
        });

        it('setWaiverAccepted(id, false) removes from acceptance map', () => {
            service.setBindings({ waiver1: 'chkWaiver' });
            service.setWaiverAccepted('waiver1', true);
            expect(service.waiversAccepted()['chkWaiver']).toBe(true);

            service.setWaiverAccepted('waiver1', false);

            const accepted = service.waiversAccepted();
            expect(accepted['chkWaiver']).toBeUndefined();
            expect(accepted['waiver1']).toBeUndefined();
        });

        it('isWaiverAccepted returns true after accepting', () => {
            service.setBindings({ waiver1: 'chkWaiver' });
            service.setWaiverAccepted('waiver1', true);

            expect(service.isWaiverAccepted('waiver1')).toBe(true);
        });

        it('isWaiverAccepted returns false before accepting', () => {
            service.setBindings({ waiver1: 'chkWaiver' });

            expect(service.isWaiverAccepted('waiver1')).toBe(false);
        });

        it('allRequiredWaiversAccepted returns true when all required accepted', () => {
            const defs = [
                makeDef({ id: 'w1', required: true }),
                makeDef({ id: 'w2', required: true }),
            ];
            service.setDefinitions(defs);
            service.setBindings({ w1: 'chk1', w2: 'chk2' });

            service.setWaiverAccepted('w1', true);
            service.setWaiverAccepted('w2', true);

            expect(service.allRequiredWaiversAccepted()).toBe(true);
        });
    });

    // ── 2. allRequiredWaiversAccepted ───────────────────────────────────

    describe('allRequiredWaiversAccepted', () => {
        it('returns true when no definitions exist', () => {
            expect(service.allRequiredWaiversAccepted()).toBe(true);
        });

        it('returns true when all required waivers are accepted', () => {
            service.setDefinitions([
                makeDef({ id: 'w1', required: true }),
                makeDef({ id: 'w2', required: false }),
            ]);
            service.setBindings({ w1: 'chk1' });
            service.setWaiverAccepted('w1', true);

            expect(service.allRequiredWaiversAccepted()).toBe(true);
        });

        it('returns false when some required waivers are not accepted', () => {
            service.setDefinitions([
                makeDef({ id: 'w1', required: true }),
                makeDef({ id: 'w2', required: true }),
            ]);
            service.setBindings({ w1: 'chk1', w2: 'chk2' });
            service.setWaiverAccepted('w1', true);
            // w2 not accepted

            expect(service.allRequiredWaiversAccepted()).toBe(false);
        });
    });

    // ── 3. detectWaiverFieldsFromSchemas (via processSchemasAndBindWaivers) ─

    describe('detectWaiverFieldsFromSchemas (via processSchemasAndBindWaivers)', () => {
        const players = [makePlayer({ playerId: 'p1', registered: false })];

        it('detects required checkbox fields as waiver fields', () => {
            const schemas = [
                makeSchema({ name: 'chkAgree', label: 'I agree to terms', type: 'checkbox', required: true }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            expect(service.waiverFieldNames()).toContain('chkAgree');
        });

        it('detects checkbox with "I agree" prefix', () => {
            const schemas = [
                makeSchema({ name: 'chkWaiver', label: 'I agree to the waiver', type: 'checkbox', required: false }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            expect(service.waiverFieldNames()).toContain('chkWaiver');
        });

        it('detects checkbox with "waiver" in label', () => {
            const schemas = [
                makeSchema({ name: 'chkRelease', label: 'Player waiver acceptance', type: 'checkbox', required: false }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            expect(service.waiverFieldNames()).toContain('chkRelease');
        });

        it('ignores non-checkbox fields without waiver keywords in name', () => {
            const schemas = [
                makeSchema({ name: 'playerNotes', label: 'Additional Notes', type: 'text', required: true }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            expect(service.waiverFieldNames()).not.toContain('playerNotes');
        });
    });

    // ── 4. buildFromMetadata ────────────────────────────────────────────

    describe('buildFromMetadata', () => {
        const players = [makePlayer({ playerId: 'p1', registered: false })];

        it('extracts PlayerReg* keys from metadata as waiver text', () => {
            const meta: Record<string, unknown> = {
                PlayerRegReleaseOfLiability: '<p>Release text</p>',
                PlayerRegCodeOfConduct: '<p>Code of conduct</p>',
                SomeOtherKey: 'not a waiver',
            };
            const profileMeta = JSON.stringify([
                { name: 'chkWaiver', label: 'I agree to waiver release', type: 'checkbox' },
                { name: 'chkConduct', label: 'I agree to the code of conduct', type: 'checkbox' },
            ]);

            const result = service.buildFromMetadata(meta, profileMeta, ['p1'], players);

            expect(result['PlayerRegReleaseOfLiability']).toBe('<p>Release text</p>');
            expect(result['PlayerRegCodeOfConduct']).toBe('<p>Code of conduct</p>');
            expect(result['SomeOtherKey']).toBeUndefined();
        });

        it('creates definitions gated by acceptance checkbox in schema', () => {
            const meta: Record<string, unknown> = {
                PlayerRegReleaseOfLiability: '<p>Release text</p>',
            };
            const profileMeta = JSON.stringify([
                { name: 'chkWaiver', label: 'Waiver release acceptance', type: 'checkbox' },
            ]);

            service.buildFromMetadata(meta, profileMeta, ['p1'], players);

            const defs = service.waiverDefinitions();
            expect(defs.length).toBeGreaterThanOrEqual(1);
            const releaseDef = defs.find(d => d.id === 'PlayerRegReleaseOfLiability');
            expect(releaseDef).toBeDefined();
            expect(releaseDef!.title).toBe('Player Waiver');
            expect(releaseDef!.required).toBe(true);
        });

        it('returns waiver text map', () => {
            const meta: Record<string, unknown> = {
                PlayerRegRefundPolicy: '<p>Refund terms</p>',
            };
            const profileMeta = JSON.stringify([
                { name: 'chkRefund', label: 'I agree to refund policy', type: 'checkbox' },
            ]);

            const result = service.buildFromMetadata(meta, profileMeta, ['p1'], players);

            expect(result).toEqual(expect.objectContaining({
                PlayerRegRefundPolicy: '<p>Refund terms</p>',
            }));
        });
    });

    // ── 5. recomputeWaiverAcceptanceOnSelectionChange ────────────────────

    describe('recomputeWaiverAcceptanceOnSelectionChange', () => {
        const defs = [
            makeDef({ id: 'w1', required: true }),
            makeDef({ id: 'w2', required: true }),
        ];

        beforeEach(() => {
            service.setDefinitions(defs);
            service.setBindings({ w1: 'chk1', w2: 'chk2' });
        });

        it('pre-fills acceptance when all selected players are registered', () => {
            const players = [
                makePlayer({ playerId: 'p1', registered: true }),
                makePlayer({ playerId: 'p2', registered: true }),
            ];

            service.recomputeWaiverAcceptanceOnSelectionChange(['p1', 'p2'], players);

            const accepted = service.waiversAccepted();
            expect(accepted['chk1']).toBe(true);
            expect(accepted['chk2']).toBe(true);
        });

        it('clears acceptance when any selected player is unregistered', () => {
            // First seed some acceptance
            service.setWaiverAccepted('w1', true);
            service.setWaiverAccepted('w2', true);

            const players = [
                makePlayer({ playerId: 'p1', registered: true }),
                makePlayer({ playerId: 'p2', registered: false }),
            ];

            service.recomputeWaiverAcceptanceOnSelectionChange(['p1', 'p2'], players);

            expect(Object.keys(service.waiversAccepted()).length).toBe(0);
        });

        it('clears signature when unregistered player present', () => {
            service.setSignatureName('John Doe');
            service.setSignatureRole('Parent/Guardian');
            // Seed acceptance so it gets cleared
            service.setWaiverAccepted('w1', true);

            const players = [
                makePlayer({ playerId: 'p1', registered: false }),
            ];

            service.recomputeWaiverAcceptanceOnSelectionChange(['p1'], players);

            expect(service.signatureName()).toBe('');
            expect(service.signatureRole()).toBe('');
        });
    });

    // ── 6. synthesizeDefinitions (via processSchemasAndBindWaivers) ──────

    describe('synthesizeDefinitions (via processSchemasAndBindWaivers)', () => {
        const players = [makePlayer({ playerId: 'p1', registered: false })];

        it('creates definitions from detected labels when no defs provided', () => {
            const schemas = [
                makeSchema({ name: 'chkWaiver', label: 'I agree to the player waiver', type: 'checkbox', required: true }),
                makeSchema({ name: 'chkConduct', label: 'I agree to code of conduct', type: 'checkbox', required: true }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            const defs = service.waiverDefinitions();
            expect(defs.length).toBeGreaterThanOrEqual(2);
        });

        it('recognizes code of conduct, refund, and waiver/release patterns', () => {
            const schemas = [
                makeSchema({ name: 'chkCode', label: 'I agree to the code of conduct', type: 'checkbox', required: true }),
                makeSchema({ name: 'chkRefund', label: 'I agree to refund policy', type: 'checkbox', required: true }),
                makeSchema({ name: 'chkWaiver', label: 'I agree to the waiver release', type: 'checkbox', required: true }),
            ];

            service.processSchemasAndBindWaivers([], schemas, ['p1'], players);

            const defs = service.waiverDefinitions();
            const ids = defs.map(d => d.id);
            expect(ids).toContain('PlayerRegCodeOfConduct');
            expect(ids).toContain('PlayerRegRefundTerms');
            expect(ids).toContain('PlayerRegReleaseOfLiability');
        });
    });
});
