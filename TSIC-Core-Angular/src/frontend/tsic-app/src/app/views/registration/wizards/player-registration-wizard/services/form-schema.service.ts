import { Injectable, signal } from '@angular/core';
import type { PlayerProfileFieldSchema } from '../registration-wizard.service';
import type { RawProfileField, RawOptionItem } from '../../shared/types/wizard.types';

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
            const getMappedOptionSetKey = (name: string, label: string): string | null => {
                const l = (label || name || '').toLowerCase();
                if (!l) return null;
                if (l.includes('kilt')) return 'ListSizes_Kilt';
                if (l.includes('jersey')) return 'ListSizes_Jersey';
                if (l.includes('short')) return 'ListSizes_Shorts';
                if (l.includes('t-shirt') || l.includes('tshirt') || l.includes('long sleeve')) return 'ListSizes_Tshirt';
                if (l.includes('reversible')) return 'ListSizes_Reversible';
                if (l.includes('position')) return 'List_Positions';
                if (l.includes('grad') && l.includes('year')) return 'List_GradYears';
                return null;
            };
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
                if (name.toLowerCase() === 'sportassnid' || name.toLowerCase() === 'uslax' || label.toLowerCase().includes('lacrosse')) type = 'text';
                const required = !!(f.required || f?.validation?.required || f?.validation?.requiredTrue);
                const dsKey = String(f.dataSource || f.optionsSource || f.optionSet || '').trim();
                const options = (() => {
                    let mapped: string[] = [];
                    const direct = Array.isArray(f.options) ? f.options : [];
                    mapped = direct.map((o: RawOptionItem) => String(o?.value ?? o?.Value ?? o?.label ?? o?.Text ?? o));
                    if ((!mapped || mapped.length === 0) && optionSets && dsKey) {
                        const setVal = optionSets[dsKey] ?? getOptionSetInsensitive(dsKey) ?? null;
                        if (Array.isArray(setVal)) mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v?.id ?? v?.Id ?? v?.code ?? v?.Code ?? v?.year ?? v?.Year ?? v));
                    }
                    if ((!mapped || mapped.length === 0) && optionSets) {
                        const key = getMappedOptionSetKey(name, label);
                        const setVal = key ? getOptionSetInsensitive(key) : null;
                        if (Array.isArray(setVal)) mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v));
                    }
                    if ((!mapped || mapped.length === 0) && optionSets && name) {
                        const key = Object.keys(optionSets).find(k => k.toLowerCase() === name.toLowerCase());
                        const setVal = key ? optionSets[key] : null;
                        if (Array.isArray(setVal)) mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v));
                    }
                    if ((!mapped || mapped.length === 0) && optionSets && (type === 'select' || type === 'multiselect')) {
                        const lname = (label || name).toLowerCase();
                        if (lname.includes('kilt')) {
                            const k = Object.keys(optionSets).find(x => x.toLowerCase().includes('kilt'));
                            const setVal = k ? optionSets[k] : null;
                            if (Array.isArray(setVal)) mapped = setVal.map(v => String(v?.value ?? v?.Value ?? v));
                        }
                    }
                    return mapped;
                })();
                const helpText = f.helpText || f.help || f.placeholder || null;
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
                return { name, label, type, required, options, helpText, visibility, condition } as PlayerProfileFieldSchema;
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