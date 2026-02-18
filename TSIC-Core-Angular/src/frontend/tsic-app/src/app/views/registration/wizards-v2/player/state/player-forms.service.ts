import { Injectable, inject, signal } from '@angular/core';
import { FormSchemaService } from '@views/registration/wizards/player-registration-wizard/services/form-schema.service';
import { hasAllParts } from '@views/registration/wizards/shared/utils/property-utils';
import type {
    PlayerProfileFieldSchema,
    PlayerFormFieldValue,
    UsLaxStatusEntry,
    FamilyPlayerDto,
    Json,
} from '../types/player-wizard.types';

/**
 * Player Forms Service — owns per-player form values, validation,
 * server validation errors, and US Lax status.
 *
 * Extracted from RegistrationWizardService lines ~121-127, 543-670, 709-728, 838-926, 1142-1280.
 * Gold-standard signal pattern throughout.
 */
@Injectable({ providedIn: 'root' })
export class PlayerFormsService {
    private readonly formSchema = inject(FormSchemaService);

    // ── Form values (playerId -> fieldName -> value) ──────────────────
    private readonly _playerFormValues = signal<Record<string, Record<string, PlayerFormFieldValue>>>({});
    readonly playerFormValues = this._playerFormValues.asReadonly();

    // ── US Lacrosse validation status ─────────────────────────────────
    private readonly _usLaxStatus = signal<Record<string, UsLaxStatusEntry>>({});
    readonly usLaxStatus = this._usLaxStatus.asReadonly();

    // ── Controlled mutators ───────────────────────────────────────────
    setPlayerFieldValue(playerId: string, fieldName: string, value: PlayerFormFieldValue): void {
        const all = { ...this._playerFormValues() };
        if (!all[playerId]) all[playerId] = {};
        all[playerId] = { ...all[playerId], [fieldName]: value };
        this._playerFormValues.set(all);

        // Track US Lacrosse number
        if (fieldName.toLowerCase() === 'sportassnid') {
            const statusMap = { ...this._usLaxStatus() };
            const existing = statusMap[playerId] || { value: '', status: 'idle' as const };
            const raw = String(value ?? '').trim();
            if (raw === '424242424242') {
                statusMap[playerId] = { ...existing, value: raw, status: 'valid', message: 'Test US Lax number accepted' };
            } else {
                statusMap[playerId] = { ...existing, value: raw, status: 'idle', message: undefined };
            }
            this._usLaxStatus.set(statusMap);
        }
    }

    getPlayerFieldValue(playerId: string, fieldName: string): unknown {
        return this._playerFormValues()[playerId]?.[fieldName];
    }

    setUsLaxValidating(playerId: string): void {
        this.updateUsLaxEntry(playerId, { status: 'validating', message: undefined });
    }

    setUsLaxResult(playerId: string, ok: boolean, message?: string, membership?: Record<string, unknown>): void {
        this.updateUsLaxEntry(playerId, { status: ok ? 'valid' : 'invalid', message, membership });
    }

    private updateUsLaxEntry(playerId: string, patch: Partial<UsLaxStatusEntry>): void {
        const m = { ...this._usLaxStatus() };
        const cur = m[playerId] || { value: '', status: 'idle' as const };
        m[playerId] = { ...cur, ...patch };
        this._usLaxStatus.set(m);
    }

    // ── Form initialization (after metadata load) ─────────────────────
    initializeFormValuesForSelectedPlayers(
        schemas: PlayerProfileFieldSchema[],
        selectedIds: string[],
    ): void {
        const current = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        for (const pid of selectedIds) {
            if (!current[pid]) current[pid] = {};
            for (const f of schemas) {
                if (!(f.name in current[pid])) current[pid][f.name] = null;
            }
        }
        this._playerFormValues.set(current);
    }

    /** Seed form values from prior registrations (registered players). */
    seedFromPriorRegistrations(
        schemas: PlayerProfileFieldSchema[],
        players: FamilyPlayerDto[],
    ): void {
        try {
            const current = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            const schemaNameByLower = this.buildSchemaLookup(schemas);
            for (const player of players) {
                if (!player.registered || !player.priorRegistrations?.length) continue;
                if (!current[player.playerId]) continue;
                const latestRegistration = player.priorRegistrations.at(-1);
                const formFieldValues = latestRegistration?.formFieldValues || {};
                this.copyFormFieldValues(formFieldValues, current[player.playerId], schemaNameByLower);
            }
            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[PlayerForms] Prior registration seed failed', e);
        }
    }

    /** Seed default values for unregistered selected players. */
    seedFromDefaults(
        schemas: PlayerProfileFieldSchema[],
        players: FamilyPlayerDto[],
    ): void {
        try {
            const current = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            const schemaNameByLower = this.buildSchemaLookup(schemas);
            for (const player of players) {
                if (player.registered || !player.selected) continue;
                this.applyDefaultsToPlayer(player, current, schemaNameByLower);
            }
            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[PlayerForms] Default values seed failed', e);
        }
    }

    /** Apply alias backfill (if alias source present but target not). */
    applyAliasBackfill(): void {
        try {
            const alias = this.formSchema.aliasFieldMap();
            if (!alias || !Object.keys(alias).length) return;
            const current = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            for (const [, vals] of Object.entries(current)) {
                for (const [from, to] of Object.entries(alias)) {
                    if (from in vals && !(to in vals)) vals[to] = vals[from];
                }
            }
            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[PlayerForms] Alias backfill failed', e);
        }
    }

    /** Initialize form fields + defaults for a single newly-selected player. */
    initializePlayerFormDefaults(playerId: string, schemas: PlayerProfileFieldSchema[], players: FamilyPlayerDto[]): void {
        try {
            this.initializeFormFieldsForPlayer(playerId, schemas);
            this.applyDefaultValuesForPlayer(playerId, schemas, players);
        } catch { /* ignore */ }
    }

    /** Prune form values for deselected players. */
    pruneDeselectedPlayers(selectedIds: Set<string>): void {
        const forms = { ...this._playerFormValues() };
        let changed = false;
        for (const pid of Object.keys(forms)) {
            if (!selectedIds.has(pid)) {
                delete forms[pid];
                changed = true;
            }
        }
        if (changed) this._playerFormValues.set(forms);
    }

    // ── Validation ────────────────────────────────────────────────────
    /** Returns map of playerId -> fieldName -> error message. */
    validateAllSelectedPlayers(
        schemas: PlayerProfileFieldSchema[],
        selectedPlayerIds: string[],
        isLocked: (pid: string) => boolean,
        isVisible: (pid: string, field: PlayerProfileFieldSchema) => boolean,
    ): Record<string, Record<string, string>> {
        const errors: Record<string, Record<string, string>> = {};
        for (const pid of selectedPlayerIds) {
            for (const field of schemas) {
                const msg = this.validateFieldForPlayer(pid, field, isLocked, isVisible);
                if (msg) {
                    if (!errors[pid]) errors[pid] = {};
                    errors[pid][field.name] = msg;
                }
            }
        }
        return errors;
    }

    /** True when all visible required fields are valid for all selected non-locked players. */
    areFormsValid(
        schemas: PlayerProfileFieldSchema[],
        selectedPlayerIds: string[],
        isLocked: (pid: string) => boolean,
        isVisible: (pid: string, field: PlayerProfileFieldSchema) => boolean,
    ): boolean {
        for (const pid of selectedPlayerIds) {
            if (isLocked(pid)) continue;
            for (const field of schemas) {
                if (this.validateFieldForPlayer(pid, field, isLocked, isVisible)) return false;
            }
        }
        return true;
    }

    /** Per-player validity. */
    arePlayerFormsValid(
        playerId: string,
        schemas: PlayerProfileFieldSchema[],
        isLocked: (pid: string) => boolean,
        isVisible: (pid: string, field: PlayerProfileFieldSchema) => boolean,
    ): boolean {
        if (isLocked(playerId)) return true;
        for (const field of schemas) {
            if (this.validateFieldForPlayer(playerId, field, isLocked, isVisible)) return false;
        }
        return true;
    }

    private validateFieldForPlayer(
        playerId: string,
        field: PlayerProfileFieldSchema,
        isLocked: (pid: string) => boolean,
        isVisible: (pid: string, f: PlayerProfileFieldSchema) => boolean,
    ): string | null {
        if (!isVisible(playerId, field) || isLocked(playerId)) return null;
        const raw = this.getPlayerFieldValue(playerId, field.name);
        const str = raw == null ? '' : String(raw).trim();
        if (this.isRequiredInvalid(field, raw, str)) return 'Required';
        const typeError = this.validateBasicType(field, raw, str);
        if (typeError) return typeError;
        if (this.isUsLaxSchemaField(field)) return this.validateUsLaxField(playerId, field, str);
        return null;
    }

    private isRequiredInvalid(field: PlayerProfileFieldSchema, raw: unknown, str: string): boolean {
        if (!field.required) return false;
        if (field.type === 'checkbox') return raw !== true;
        if (field.type === 'multiselect') return !Array.isArray(raw) || raw.length === 0;
        return str.length === 0;
    }

    private validateBasicType(field: PlayerProfileFieldSchema, raw: unknown, str: string): string | null {
        if (str.length === 0 && field.type !== 'multiselect') return null;
        switch (field.type) {
            case 'number':
                if (str.length && Number.isNaN(Number(str))) return 'Must be a number';
                return null;
            case 'date':
                if (str.length) {
                    const dt = new Date(str);
                    if (Number.isNaN(dt.getTime())) return 'Invalid date';
                }
                return null;
            case 'select':
                if (field.options?.length && str.length && !field.options.includes(str)) return 'Invalid option';
                return null;
            case 'multiselect':
                if (!Array.isArray(raw)) return field.required ? 'Required' : null;
                if (field.options?.length && raw.some((v: unknown) => !field.options.includes(String(v)))) return 'Invalid option';
                if (field.required && raw.length === 0) return 'Required';
                return null;
            default:
                return null;
        }
    }

    private isUsLaxSchemaField(field: PlayerProfileFieldSchema): boolean {
        const lname = field.name.toLowerCase();
        const llabel = field.label.toLowerCase();
        return lname === 'sportassnid' || llabel.includes('lacrosse');
    }

    private validateUsLaxField(playerId: string, field: PlayerProfileFieldSchema, strVal: string): string | null {
        const statusEntry = this._usLaxStatus()[playerId];
        const status = statusEntry?.status || 'idle';
        if (strVal === '424242424242') return null;
        if (field.required && strVal.length === 0) return 'Required';
        if (!field.required && strVal.length === 0) return null;
        if (status === 'validating') return 'Validating…';
        if (status === 'invalid') return statusEntry?.message || 'Invalid membership';
        if (status !== 'valid') return 'Membership not validated';
        return null;
    }

    // ── Visibility ────────────────────────────────────────────────────
    /** Central visibility logic. waiverFieldNames + teamConstraintType come from JobContextService. */
    isFieldVisibleForPlayer(
        playerId: string,
        field: PlayerProfileFieldSchema,
        waiverFieldNames: string[],
        teamConstraintType: string | null,
    ): boolean {
        if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
        if (waiverFieldNames.includes(field.name)) return false;
        const lname = field.name.toLowerCase();
        const llabel = field.label.toLowerCase();
        if (['team', 'teamid', 'teams'].includes(lname) || llabel.includes('select a team')) return false;
        if (lname === 'eligibility' || llabel.includes('eligibility')) return false;
        const tctype = (teamConstraintType || '').toUpperCase();
        if (tctype === 'BYGRADYEAR' && (hasAllParts(lname, ['grad', 'year']) || hasAllParts(llabel, ['grad', 'year']))) return false;
        if (tctype === 'BYAGEGROUP' && (hasAllParts(lname, ['age', 'group']) || hasAllParts(llabel, ['age', 'group']))) return false;
        if (tctype === 'BYAGERANGE' && (hasAllParts(lname, ['age', 'range']) || hasAllParts(llabel, ['age', 'range']))) return false;
        if (!field.condition) return true;
        const otherVal = this.getPlayerFieldValue(playerId, field.condition.field);
        const op = (field.condition.operator || 'equals').toLowerCase();
        if (op === 'equals') return otherVal === field.condition.value;
        return otherVal === field.condition.value;
    }

    // ── PreSubmit form value builders ─────────────────────────────────
    /** Build visible (non-hidden, non-adminOnly, non-waiver) form values for a player. */
    buildVisibleFormValuesForPlayer(
        playerId: string,
        schemas: PlayerProfileFieldSchema[],
        waiverFieldNames: string[],
    ): { [key: string]: Json } {
        const hidden = new Set(waiverFieldNames.map(n => n.toLowerCase()));
        const visibleNames = new Set(
            schemas
                .filter(s => (s.visibility ?? 'public') !== 'hidden' && (s.visibility ?? 'public') !== 'adminOnly')
                .map(s => s.name),
        );
        const all = this._playerFormValues()[playerId] || {} as Record<string, unknown>;
        const out: { [k: string]: Json } = {};
        for (const [k, v] of Object.entries(all)) {
            if (!visibleNames.has(k)) continue;
            if (hidden.has(k.toLowerCase())) continue;
            if (v === undefined) continue;
            out[k] = v as Json;
        }
        return out;
    }

    /** Build form values + waiver + eligibility fields for preSubmit payload. */
    buildPreSubmitFormValuesForPlayer(
        playerId: string,
        schemas: PlayerProfileFieldSchema[],
        waiverFieldNames: string[],
        waiversGateOk: boolean,
        isWaiverAccepted: (key: string) => boolean,
        getEligibilityForPlayer: (pid: string) => string | undefined,
        resolveEligibilityFieldName: () => string | null,
    ): { [key: string]: Json } {
        const out = this.buildVisibleFormValuesForPlayer(playerId, schemas, waiverFieldNames);

        // Inject eligibility field
        try {
            const eligField = resolveEligibilityFieldName();
            if (eligField) {
                const existing = out[eligField];
                const isMissing = existing == null || (typeof existing === 'string' && existing.trim() === '');
                const selected = getEligibilityForPlayer(playerId);
                if (isMissing && selected != null && String(selected).trim() !== '') {
                    out[eligField] = String(selected);
                }
            }
        } catch { /* best-effort */ }

        // Inject waiver acceptance booleans
        const names = Array.isArray(waiverFieldNames) ? [...waiverFieldNames] : [];
        if (names.length === 0) return out;
        const seen = new Set<string>();
        const deduped = names.filter(n => {
            const k = String(n || '').toLowerCase();
            if (!k || seen.has(k)) return false;
            seen.add(k);
            return true;
        });
        for (const name of deduped) {
            if (waiversGateOk || isWaiverAccepted(name)) {
                out[name] = true;
            }
        }
        return out;
    }

    // ── Private helpers ───────────────────────────────────────────────
    private buildSchemaLookup(schemas: PlayerProfileFieldSchema[]): Record<string, string> {
        const lookup: Record<string, string> = {};
        for (const s of schemas) lookup[s.name.toLowerCase()] = s.name;
        return lookup;
    }

    private copyFormFieldValues(
        source: Record<string, unknown>,
        target: Record<string, unknown>,
        schemaNameByLower: Record<string, string>,
    ): void {
        for (const [rawKey, rawValue] of Object.entries(source)) {
            if (rawValue == null || rawValue === '') continue;
            const targetName = this.resolveFieldName(rawKey, schemaNameByLower);
            if (targetName) target[targetName] = rawValue;
        }
    }

    private applyDefaultsToPlayer(
        player: FamilyPlayerDto,
        formValues: Record<string, Record<string, unknown>>,
        schemaNameByLower: Record<string, string>,
    ): void {
        const pid = player.playerId;
        if (!formValues[pid]) return;
        const defaults = player.defaultFieldValues || {};
        for (const [rawKey, rawValue] of Object.entries(defaults)) {
            if (rawValue == null || rawValue === '') continue;
            const targetName = this.resolveFieldName(rawKey, schemaNameByLower);
            if (!targetName) continue;
            if (this.isFieldValueBlank(formValues[pid][targetName])) {
                formValues[pid][targetName] = rawValue;
            }
        }
    }

    private initializeFormFieldsForPlayer(playerId: string, schemas: PlayerProfileFieldSchema[]): void {
        const current = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        if (!current[playerId]) current[playerId] = {};
        for (const f of schemas) {
            if (!(f.name in current[playerId])) current[playerId][f.name] = null;
        }
        this._playerFormValues.set(current);
    }

    private applyDefaultValuesForPlayer(
        playerId: string,
        schemas: PlayerProfileFieldSchema[],
        players: FamilyPlayerDto[],
    ): void {
        const fam = players.find(p => p.playerId === playerId);
        if (!fam || fam.registered || !fam.defaultFieldValues) return;
        const df = fam.defaultFieldValues;
        const schemaNameByLower: Record<string, string> = {};
        for (const s of schemas) schemaNameByLower[s.name.toLowerCase()] = s.name;
        const alias = this.formSchema.aliasFieldMap();
        const curVals = { ...this._playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        const target = curVals[playerId];
        for (const [rawK, rawV] of Object.entries(df)) {
            if (rawV == null || rawV === '') continue;
            const targetName = this.resolveFieldName(rawK, schemaNameByLower, alias);
            if (!targetName) continue;
            if (this.isFieldValueBlank(target[targetName])) {
                target[targetName] = rawV as PlayerFormFieldValue;
            }
        }
        this._playerFormValues.set(curVals);
    }

    private resolveFieldName(
        rawKey: string,
        schemaNameByLower: Record<string, string>,
        alias?: Record<string, string>,
    ): string | null {
        const kLower = rawKey.toLowerCase();
        let targetName = schemaNameByLower[kLower];
        if (!targetName) {
            const aliasMap = alias ?? this.formSchema.aliasFieldMap();
            const foundAlias = Object.keys(aliasMap).find(a => a.toLowerCase() === kLower);
            if (foundAlias) targetName = aliasMap[foundAlias];
        }
        return targetName || null;
    }

    private isFieldValueBlank(value: unknown): boolean {
        return value == null ||
            (typeof value === 'string' && value.trim() === '') ||
            (Array.isArray(value) && value.length === 0);
    }

    // ── Reset ─────────────────────────────────────────────────────────
    reset(): void {
        this._playerFormValues.set({});
        this._usLaxStatus.set({});
    }
}
