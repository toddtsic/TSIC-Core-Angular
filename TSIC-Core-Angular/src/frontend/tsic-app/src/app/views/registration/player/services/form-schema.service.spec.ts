import { TestBed } from '@angular/core/testing';
import { FormSchemaService } from './form-schema.service';

describe('FormSchemaService', () => {
    let service: FormSchemaService;

    beforeEach(() => {
        TestBed.configureTestingModule({});
        service = TestBed.inject(FormSchemaService);
    });

    // ── Basic Parsing ────────────────────────────────────────────────

    describe('basic parsing', () => {
        it('should set empty schemas when input is null', () => {
            service.parse(null, null);

            expect(service.profileFieldSchemas()).toEqual([]);
            expect(service.aliasFieldMap()).toEqual({});
        });

        it('should parse a top-level array of fields', () => {
            const raw = JSON.stringify([
                { name: 'firstName', label: 'First Name', type: 'text', required: true },
                { name: 'lastName', label: 'Last Name', type: 'text', required: false },
            ]);

            service.parse(raw, null);

            const schemas = service.profileFieldSchemas();
            expect(schemas).toHaveLength(2);
            expect(schemas[0].name).toBe('firstName');
            expect(schemas[0].label).toBe('First Name');
            expect(schemas[0].type).toBe('text');
            expect(schemas[0].required).toBe(true);
            expect(schemas[1].name).toBe('lastName');
            expect(schemas[1].required).toBe(false);
        });

        it('should parse an object with a "fields" property', () => {
            const raw = JSON.stringify({
                fields: [
                    { name: 'email', label: 'Email', type: 'text', required: true },
                ],
            });

            service.parse(raw, null);

            const schemas = service.profileFieldSchemas();
            expect(schemas).toHaveLength(1);
            expect(schemas[0].name).toBe('email');
        });

        it('should return empty schemas for malformed JSON without throwing', () => {
            expect(() => service.parse('{{not valid json', null)).not.toThrow();

            expect(service.profileFieldSchemas()).toEqual([]);
            expect(service.aliasFieldMap()).toEqual({});
        });
    });

    // ── Type Mapping ─────────────────────────────────────────────────

    describe('type mapping', () => {
        function parseFieldType(rawType: string): string {
            const raw = JSON.stringify([{ name: 'testField', label: 'Test', type: rawType }]);
            service.parse(raw, null);
            return service.profileFieldSchemas()[0].type;
        }

        it('should map "text" and "string" to "text"', () => {
            expect(parseFieldType('text')).toBe('text');
            expect(parseFieldType('string')).toBe('text');
        });

        it('should map "int", "integer", and "number" to "number"', () => {
            expect(parseFieldType('int')).toBe('number');
            expect(parseFieldType('integer')).toBe('number');
            expect(parseFieldType('number')).toBe('number');
        });

        it('should map "date" and "datetime" to "date"', () => {
            expect(parseFieldType('date')).toBe('date');
            expect(parseFieldType('datetime')).toBe('date');
        });

        it('should map "select" and "dropdown" to "select"', () => {
            expect(parseFieldType('select')).toBe('select');
            expect(parseFieldType('dropdown')).toBe('select');
        });

        it('should map "checkbox", "bool", and "boolean" to "checkbox"', () => {
            expect(parseFieldType('checkbox')).toBe('checkbox');
            expect(parseFieldType('bool')).toBe('checkbox');
            expect(parseFieldType('boolean')).toBe('checkbox');
        });

        it('should map "multiselect" to "multiselect"', () => {
            expect(parseFieldType('multiselect')).toBe('multiselect');
        });
    });

    // ── US Lacrosse Detection ────────────────────────────────────────

    describe('US Lacrosse detection', () => {
        it('should detect sportassnid field and override type/label/remoteUrl', () => {
            const raw = JSON.stringify([
                { name: 'sportassnid', label: 'Sport Association ID', type: 'select' },
            ]);

            service.parse(raw, null);

            const schema = service.profileFieldSchemas()[0];
            expect(schema.type).toBe('text');
            expect(schema.label).toBe('USA Lacrosse Number');
            expect(schema.remoteUrl).toBe('/api/validation/uslax');
        });

        it('should detect field with "lacrosse" in label', () => {
            const raw = JSON.stringify([
                { name: 'memberNumber', label: 'US Lacrosse Membership', type: 'text' },
            ]);

            service.parse(raw, null);

            const schema = service.profileFieldSchemas()[0];
            expect(schema.label).toBe('USA Lacrosse Number');
            expect(schema.remoteUrl).toBe('/api/validation/uslax');
        });

        it('should not set remoteUrl for regular fields', () => {
            const raw = JSON.stringify([
                { name: 'firstName', label: 'First Name', type: 'text' },
            ]);

            service.parse(raw, null);

            const schema = service.profileFieldSchemas()[0];
            expect(schema.remoteUrl).toBeNull();
        });
    });

    // ── Numeric Column Inference ─────────────────────────────────────

    describe('numeric column inference', () => {
        it('should infer "number" type for known numeric dbColumn "weightlbs"', () => {
            const raw = JSON.stringify([
                { name: 'weight', dbColumn: 'weightlbs', label: 'Weight', type: 'text' },
            ]);

            service.parse(raw, null);

            expect(service.profileFieldSchemas()[0].type).toBe('number');
        });

        it('should keep "text" type for non-numeric dbColumn "firstName"', () => {
            const raw = JSON.stringify([
                { name: 'first', dbColumn: 'firstName', label: 'First Name', type: 'text' },
            ]);

            service.parse(raw, null);

            expect(service.profileFieldSchemas()[0].type).toBe('text');
        });
    });

    // ── Option Resolution ────────────────────────────────────────────

    describe('option resolution', () => {
        it('should map direct options array', () => {
            const raw = JSON.stringify([
                {
                    name: 'position',
                    label: 'Position',
                    type: 'select',
                    options: [{ value: 'goalie' }, { value: 'attack' }],
                },
            ]);

            service.parse(raw, null);

            expect(service.profileFieldSchemas()[0].options).toEqual(['goalie', 'attack']);
        });

        it('should resolve options from optionSets via dataSource key', () => {
            const rawFields = JSON.stringify([
                { name: 'gradYear', label: 'Grad Year', type: 'select', dataSource: 'GradYears' },
            ]);
            const rawOptions = JSON.stringify({
                GradYears: [{ value: '2026' }, { value: '2027' }, { value: '2028' }],
            });

            service.parse(rawFields, rawOptions);

            expect(service.profileFieldSchemas()[0].options).toEqual(['2026', '2027', '2028']);
        });

        it('should prefer dataSource shared set over stale inline options', () => {
            const rawFields = JSON.stringify([
                {
                    name: 'gradYear',
                    label: 'Grad Year',
                    type: 'select',
                    dataSource: 'List_GradYears',
                    options: [{ value: '2024' }, { value: '2025' }],
                },
            ]);
            const rawOptions = JSON.stringify({
                List_GradYears: [{ Value: '2027' }, { Value: '2028' }, { Value: '2029' }],
            });

            service.parse(rawFields, rawOptions);

            expect(service.profileFieldSchemas()[0].options).toEqual(['2027', '2028', '2029']);
        });

        it('should fall back to inline options when dataSource set is missing', () => {
            const rawFields = JSON.stringify([
                {
                    name: 'gradYear',
                    label: 'Grad Year',
                    type: 'select',
                    dataSource: 'List_GradYears',
                    options: [{ value: '2024' }, { value: '2025' }],
                },
            ]);
            // No List_GradYears in options — should fall back to inline
            const rawOptions = JSON.stringify({});

            service.parse(rawFields, rawOptions);

            expect(service.profileFieldSchemas()[0].options).toEqual(['2024', '2025']);
        });

        it('should perform case-insensitive optionSets lookup', () => {
            const rawFields = JSON.stringify([
                { name: 'size', label: 'Shirt Size', type: 'select', dataSource: 'shirtSizes' },
            ]);
            const rawOptions = JSON.stringify({
                ShirtSizes: [{ value: 'S' }, { value: 'M' }, { value: 'L' }],
            });

            service.parse(rawFields, rawOptions);

            expect(service.profileFieldSchemas()[0].options).toEqual(['S', 'M', 'L']);
        });
    });

    // ── Alias Map ────────────────────────────────────────────────────

    describe('alias map', () => {
        it('should create alias entry when dbColumn differs from name', () => {
            const raw = JSON.stringify([
                { name: 'firstName', dbColumn: 'FName', label: 'First Name', type: 'text' },
            ]);

            service.parse(raw, null);

            expect(service.aliasFieldMap()).toEqual({ FName: 'firstName' });
        });

        it('should not create alias entry when dbColumn matches name', () => {
            const raw = JSON.stringify([
                { name: 'firstName', dbColumn: 'firstName', label: 'First Name', type: 'text' },
            ]);

            service.parse(raw, null);

            expect(service.aliasFieldMap()).toEqual({});
        });
    });
});
