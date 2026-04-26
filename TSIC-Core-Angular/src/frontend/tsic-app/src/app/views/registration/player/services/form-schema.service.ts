import { Injectable, signal } from '@angular/core';
import type { PlayerProfileFieldSchema } from './registration-wizard.service';
import type { RawProfileField, RawOptionItem } from '../../shared/types/wizard.types';

// Canonical Height (Inches) fallback list when JsonOptions has no values configured.
// Range 4-0 through 6-10 (45 entries: 12+12+11) in feet-dash-inches format.
const HEIGHT_INCHES_FALLBACK: string[] = (() => {
    const out: string[] = [];
    for (let f = 4; f <= 6; f++) {
        const maxIn = f === 6 ? 10 : 11;
        for (let i = 0; i <= maxIn; i++) out.push(`${f}-${i}`);
    }
    return out;
})();

/**
 * FormSchemaService encapsulates player profile field schema & alias mapping state.
 * Extracted from RegistrationWizardService to reduce its surface area.
 * Responsibilities:
 *  - Hold parsed PlayerProfileFieldSchema[]
 *  - Hold aliasFieldMap (backend dbColumn/property -> schema field name)
 *  - Provide a parse helper that derives schemas + alias map from raw JSON + JsonOptions
 */
@Injectable({ providedIn: 'root' })
export class FormSchemaService {
    private readonly _profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
    private readonly _aliasFieldMap = signal<Record<string, string>>({});
    readonly profileFieldSchemas = this._profileFieldSchemas.asReadonly();
    readonly aliasFieldMap = this._aliasFieldMap.asReadonly();

    /** Lightweight parse: focuses only on field schemas & alias mapping. */
    parse(rawProfileMetadataJson: string | null, rawJsonOptions: string | null): void {
        if (!rawProfileMetadataJson) {
            this._profileFieldSchemas.set([]);
            this._aliasFieldMap.set({});
            return;
        }
        try {
            // Case-insensitive property picker for raw JSON fields
            const pickCI = (obj: Record<string, unknown>, ...keys: string[]): unknown => {
                for (const key of keys) {
                    if (obj[key] !== undefined && obj[key] !== null && obj[key] !== '') return obj[key];
                }
                const lowerMap = new Map(Object.keys(obj).map(k => [k.toLowerCase(), k]));
                for (const key of keys) {
                    const actual = lowerMap.get(key.toLowerCase());
                    if (actual && obj[actual] !== undefined && obj[actual] !== null && obj[actual] !== '') return obj[actual];
                }
                return undefined;
            };

            const json = JSON.parse(rawProfileMetadataJson);
            let fields: RawProfileField[] = [];
            if (Array.isArray(json)) fields = json; else if (json && Array.isArray(json.fields)) fields = json.fields; else fields = [];
            // Parse option sets (case-insensitive key access helper) for dropdown overrides
            let optionSets: Record<string, unknown> | null = null;
            if (rawJsonOptions) {
                try {
                    const parsed = JSON.parse(rawJsonOptions);
                    if (parsed && typeof parsed === 'object') optionSets = parsed;
                } catch { /* ignore malformed options */ }
            }
            const getOptionSetInsensitive = (key: string): RawOptionItem[] | null => {
                if (!optionSets || !key) return null;
                const found = Object.keys(optionSets).find(k => k.toLowerCase() === key.toLowerCase());
                return found ? optionSets[found] as RawOptionItem[] : null;
            };
            // DB columns known to be numeric on the Registrations entity
            const numericColumns = new Set([
                'weightlbs', 'gpa', 'sat', 'satmath', 'satverbal',
                'classrank', 'sportyearsexp',
            ]);
            const aliasMapLocal: Record<string, string> = {};
            const mapFieldType = (raw: string | null | undefined): PlayerProfileFieldSchema['type'] => {
                const r = String(raw || '').toLowerCase();
                switch (r) {
                    case 'text': case 'string': return 'text';
                    case 'int': case 'integer': case 'number': return 'number';
                    case 'date': case 'datetime': return 'date';
                    case 'select': case 'dropdown': return 'select';
                    case 'multiselect': case 'multi-select': return 'multiselect';
                    case 'checkbox': case 'bool': case 'boolean': return 'checkbox';
                    case 'textarea': return 'textarea';
                    case 'upload': case 'file': return 'upload';
                    default: return 'text';
                }
            };
            const schemas: PlayerProfileFieldSchema[] = fields.map(f => {
                const name = String(f.name || f.dbColumn || f.field || '');
                if (!name) return null;
                let label = String(f.label || f.displayName || f.display || f.name || name);
                const suffix = /\s*\(will not be verified at this time\)$/i;
                if (suffix.test(label)) label = label.replace(suffix, '').trim();
                const dbCol = typeof f.dbColumn === 'string' ? f.dbColumn : null;
                if (dbCol && dbCol !== name) aliasMapLocal[dbCol] = name;
                let type = mapFieldType(f.type || f.inputType);
                const isUsLaxField = name.toLowerCase() === 'sportassnid' || name.toLowerCase() === 'uslax' || label.toLowerCase().includes('lacrosse');
                if (isUsLaxField) {
                    type = 'text';
                    label = 'USA Lacrosse Number';
                }
                // bUploadedMedForm always renders as the upload control regardless of
                // stored inputType — the field name is canonical and the legacy
                // checkbox UI was client-asserted (insecure) and is being replaced.
                if (name.toLowerCase() === 'buploadedmedform') {
                    type = 'upload';
                }
                // Infer numeric type from known DB column names when metadata says 'text'
                if (type === 'text' && dbCol) {
                    const col = dbCol.toLowerCase();
                    if (numericColumns.has(col)) type = 'number';
                }
                const required = !!(f.required || f?.validation?.required || f?.validation?.requiredTrue);
                const dsKey = String(pickCI(f, 'dataSource', 'optionsSource', 'optionSet') || '').trim();
                const isHeightInchesField =
                    name.toLowerCase() === 'heightinches' || (dbCol && dbCol.toLowerCase() === 'heightinches');
                const options = (() => {
                    let mapped: string[] = [];
                    // When dataSource is set, the shared option set is authoritative — inline
                    // options on the field are a stale export artifact and must not override it.
                    if (optionSets && dsKey) {
                        const setVal = optionSets[dsKey] ?? getOptionSetInsensitive(dsKey) ?? null;
                        if (Array.isArray(setVal) && setVal.length > 0) {
                            mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v?.id ?? v?.Id ?? v?.code ?? v?.Code ?? v?.year ?? v?.Year ?? v));
                        }
                    }
                    if (mapped.length === 0) {
                        const direct = Array.isArray(f.options) ? f.options : [];
                        mapped = direct.map((o: RawOptionItem) => String(o?.value ?? o?.Value ?? o?.label ?? o?.Text ?? o));
                    }
                    if (mapped.length === 0 && optionSets && name) {
                        const key = Object.keys(optionSets).find(k => k.toLowerCase() === name.toLowerCase());
                        const setVal = key ? optionSets[key] : null;
                        if (Array.isArray(setVal)) mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v));
                    }
                    // SP-041: HeightInches fallback — when no options were provided anywhere,
                    // emit canonical 4-0..6-10 list so the field renders as a dropdown.
                    if (mapped.length === 0 && isHeightInchesField) {
                        mapped = [...HEIGHT_INCHES_FALLBACK];
                    }
                    // Sort numerically if all options are valid numbers (e.g., years of experience)
                    if (mapped.length > 1 && mapped.every(v => v !== '' && !isNaN(Number(v)))) {
                        mapped.sort((a, b) => Number(a) - Number(b));
                    }
                    return mapped;
                })();
                // SP-041: HeightInches with options must render as a dropdown even if metadata
                // says 'text' (Legacy migration left it as text input).
                if (isHeightInchesField && options.length > 0) {
                    type = 'select';
                }
                const placeholder = typeof f.placeholder === 'string' && f.placeholder.trim() ? f.placeholder.trim() : null;
                const helpText = f.helpText || f.help || null;
                const val = f.validation as Record<string, unknown> | undefined;
                const remoteUrl = (() => {
                    const v = pickCI(f, 'remoteUrl', 'remoteurl', 'remote') as string | undefined;
                    if (typeof v === 'string' && v.trim()) return v.trim();
                    // Fallback: validation.remote (migration model format)
                    const vr = val?.['remote'];
                    return typeof vr === 'string' && vr.trim() ? vr.trim() : null;
                })();
                const errorMessage = (() => {
                    const v = pickCI(f, 'errorMessage', 'errormessage') as string | undefined;
                    if (typeof v === 'string' && v.trim()) return v.trim();
                    // Fallback: validation.message (migration model format)
                    const vm = val?.['message'];
                    return typeof vm === 'string' && vm.trim() ? vm.trim() : null;
                })();
                const visibility = (() => {
                    const vis = f.visibility || f.adminOnly;
                    if (typeof vis === 'string') {
                        const lower = vis.toLowerCase();
                        if (lower === 'adminonly') return 'adminOnly';
                        if (lower === 'hidden') return 'hidden';
                        return 'public';
                    }
                    if (vis === true) return 'adminOnly';
                    return undefined;
                })();
                const condition = (() => {
                    const c = f.condition;
                    if (c && typeof c === 'object' && c.field) return { field: String(c.field), value: c.value, operator: c.operator ? String(c.operator) : undefined };
                    return null;
                })();
                // US Lax fields always use the same API validation endpoint and error message
                const finalRemoteUrl = isUsLaxField ? '/api/validation/uslax' : remoteUrl;
                const finalErrorMessage = isUsLaxField
                    ? '<strong>We encountered an issue validating the USA Lacrosse Number you entered. To successfully pass validation, please confirm the following:</strong>'
                      + '<ol><li>The USA Lacrosse Number is entered correctly</li>'
                      + '<li>The membership is Valid and Active</li>'
                      + '<li>The membership <strong>does not expire before the date required</strong> by the event or club director</li>'
                      + '<li>The <strong>Date of Birth and Last Name</strong> of the player entered above exactly match what USA Lacrosse has on file</li>'
                      + '<li>The member has completed the USA Lacrosse <strong>age verification process</strong>*</li></ol>'
                      + '*Beginning July 1, 2025, all USA Lacrosse player members are required to complete a one-time age verification process '
                      + 'to maintain an active membership. (<a href="https://www.usalacrosse.com/age-verification" target="_blank">Learn more</a>)'
                      + '<br><br><strong>Helpful Links:</strong><ul>'
                      + '<li>Look up your USA Lacrosse Number — <a href="https://account.usalacrosse.com/login/lookup" target="_blank">CLICK HERE</a></li>'
                      + '<li>Register for a USA Lacrosse Number — <a href="https://www.usalacrosse.com/membership" target="_blank">CLICK HERE</a></li></ul>'
                      + 'For assistance please contact <a href="mailto:membership@usalacrosse.com">membership@usalacrosse.com</a> or call <span style="white-space:nowrap">410-235-6882</span>'
                    : errorMessage;
                return { name, label, type, required, options, placeholder, helpText, remoteUrl: finalRemoteUrl, errorMessage: finalErrorMessage, visibility, condition } as PlayerProfileFieldSchema;
            }).filter(s => !!s?.name) as PlayerProfileFieldSchema[];
            this._profileFieldSchemas.set(schemas);
            this._aliasFieldMap.set(aliasMapLocal);
        } catch (e: unknown) {
            console.error('[FormSchemaService] Failed to parse profile metadata', e);
            this._profileFieldSchemas.set([]);
            this._aliasFieldMap.set({});
        }
    }
}