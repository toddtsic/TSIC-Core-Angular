import { Injectable, signal } from '@angular/core';
import type { WaiverDefinition } from '../registration-wizard.service';
import type { FamilyPlayerDto } from '../../../core/api/models';

/**
 * WaiverStateService: owns waiver definitions, mapping, acceptance & signature state.
 * Extracted from RegistrationWizardService to reduce its surface area.
 */
@Injectable({ providedIn: 'root' })
export class WaiverStateService {

    // Signals migrated from RegistrationWizardService
    waiverDefinitions = signal<WaiverDefinition[]>([]);
    waiverIdToField = signal<Record<string, string>>({});
    waiversAccepted = signal<Record<string, boolean>>({});
    waiverFieldNames = signal<string[]>([]);
    waiversGateOk = signal<boolean>(false);
    signatureName = signal<string>('');
    signatureRole = signal<'Parent/Guardian' | 'Adult Player' | ''>('');

    // Removed automatic effect to eliminate circular dependency with RegistrationWizardService.
    // Caller (wizard) is responsible for invoking recompute when selection or definitions change.

    setDefinitions(defs: WaiverDefinition[]): void { this.waiverDefinitions.set(defs); }
    setWaiverFieldNames(names: string[]): void { this.waiverFieldNames.set(names); }
    setBindings(map: Record<string, string>): void { this.waiverIdToField.set(map); }

    setWaiverAccepted(idOrField: string, accepted: boolean): void {
        const bindings = this.waiverIdToField();
        const field = bindings[idOrField] || idOrField;
        const map = { ...this.waiversAccepted() } as Record<string, boolean>;
        let defId: string | undefined;
        for (const [k, v] of Object.entries(bindings)) if (v === field) { defId = k; break; }
        if (accepted) {
            map[field] = true;
            if (defId) map[defId] = true; else map[idOrField] = true;
        } else {
            delete map[field];
            if (defId) delete map[defId]; else delete map[idOrField];
        }
        this.waiversAccepted.set(map);
    }

    isWaiverAccepted(key: string): boolean {
        const bindings = this.waiverIdToField();
        const field = bindings[key] || key;
        const map = this.waiversAccepted();
        return !!map[field] || !!map[key];
    }

    allRequiredWaiversAccepted(): boolean {
        const defs = this.waiverDefinitions();
        if (!defs.length) return true;
        for (const d of defs) {
            if (!d.required) continue;
            if (!this.isWaiverAccepted(d.id)) return false;
        }
        return true;
    }

    requireSignature(): boolean {
        return this.waiverDefinitions().some(w => w.required);
    }

    /** Recompute waiver acceptance when player selection changes. */
    recomputeWaiverAcceptanceOnSelectionChange(selectedPlayerIds: string[], familyPlayers: FamilyPlayerDto[]): void {
        try {
            const defs = this.waiverDefinitions();
            const selectedIds = new Set(selectedPlayerIds);
            if (!defs.length || !selectedIds.size) return;
            const players = familyPlayers.filter(p => selectedIds.has(p.playerId));
            const allSelectedRegistered = players.every(p => p.registered);
            if (allSelectedRegistered) {
                if (Object.keys(this.waiversAccepted()).length === 0) {
                    const accepted: Record<string, boolean> = {};
                    const map = this.waiverIdToField();
                    for (const d of defs) {
                        if (!d.required) continue;
                        const field = map[d.id] || d.id;
                        accepted[field] = true;
                    }
                    this.waiversAccepted.set(accepted);
                }
            } else {
                if (Object.keys(this.waiversAccepted()).length > 0) this.waiversAccepted.set({});
                if (this.signatureName()) this.signatureName.set('');
                if (this.signatureRole()) this.signatureRole.set('');
            }
        } catch { /* ignore */ }
    }

    /** Seed acceptance for read-only scenarios (all selected players registered). */
    seedAcceptedWaiversIfReadOnly(selectedPlayerIds: string[], familyPlayers: FamilyPlayerDto[]): void {
        try {
            const defs = this.waiverDefinitions();
            if (!defs.length) return;
            if (Object.keys(this.waiversAccepted()).length > 0) return;
            const selected = new Set(selectedPlayerIds);
            if (!selected.size) return;
            const players = familyPlayers.filter(p => selected.has(p.playerId));
            if (!players.every(p => p.registered)) return;
            const accepted: Record<string, boolean> = {};
            for (const d of defs) {
                if (!d.required) continue;
                const field = this.waiverIdToField()[d.id] || d.id;
                accepted[field] = true;
            }
            this.waiversAccepted.set(accepted);
        } catch { /* ignore */ }
    }

    processSchemasAndBindWaivers(defs: WaiverDefinition[], schemas: { name: string; label: string; type: string; required: boolean; visibility?: string }[], selectedPlayerIds: string[], familyPlayers: FamilyPlayerDto[]): void {
        try {
            const { fields, labels } = this.detectWaiverFieldsFromSchemas(schemas);
            this.waiverFieldNames.set(fields);
            this.buildBindings(defs, schemas, fields);
            if ((defs?.length ?? 0) === 0 && labels.length) {
                const synthesized = this.synthesizeDefinitions(labels);
                if (synthesized.length) this.waiverDefinitions.set(synthesized);
            }
            if (Object.keys(this.waiversAccepted()).length === 0) this.seedAcceptedWaiversIfReadOnly(selectedPlayerIds, familyPlayers);
        } catch (e) {
            console.debug('[WaiverState] processSchemasAndBindWaivers failed', e);
        }
    }

    private detectWaiverFieldsFromSchemas(schemas: { name: string; label: string; type: string; required: boolean; visibility?: string }[]): { fields: string[]; labels: string[] } {
        const detectedFields: string[] = [];
        const detectedLabels: string[] = [];
        const containsAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
        for (const f of schemas) {
            try {
                const lname = f.name.toLowerCase();
                const llabel = f.label.toLowerCase();
                const isCheckbox = f.type === 'checkbox';
                if (isCheckbox && f.required) {
                    detectedFields.push(f.name); detectedLabels.push(f.label); continue;
                }
                const looksLikeWaiver = isCheckbox && (
                    llabel.startsWith('i agree') ||
                    llabel.includes('waiver') || llabel.includes('release') ||
                    containsAll(llabel, ['code', 'conduct']) ||
                    llabel.includes('refund') || containsAll(llabel, ['terms', 'conditions'])
                );
                if (looksLikeWaiver || lname.includes('waiver') || lname.includes('codeofconduct') || lname.includes('refund')) {
                    detectedFields.push(f.name); detectedLabels.push(f.label);
                }
            } catch { /* single field ignore */ }
        }
        const seen = new Set<string>();
        const uniq: string[] = [];
        for (const n of detectedFields) {
            const key = n.toLowerCase();
            if (seen.has(key)) continue;
            seen.add(key); uniq.push(n);
        }
        return { fields: uniq, labels: detectedLabels };
    }

    private buildBindings(defs: WaiverDefinition[], schemas: { name: string; label: string; type: string; required: boolean; visibility?: string }[], detectedFields: string[]): void {
        try {
            const checkboxSchemas = schemas.filter(s => s.type === 'checkbox');
            const toWords = (s: string) => s.toLowerCase().split(/[^a-z0-9]+/).filter(w => w.length > 2);
            const score = (a: string, b: string) => {
                const A = new Set(toWords(a));
                const B = new Set(toWords(b));
                let m = 0;
                for (const w of A) {
                    if (B.has(w)) m++;
                }
                return m;
            };
            const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
            const pick = (def: WaiverDefinition): string | null => {
                const idL = def.id.toLowerCase();
                const titleL = def.title.toLowerCase();
                let candidates = checkboxSchemas;
                if (idL.includes('codeofconduct') || hasAll(titleL, ['code', 'conduct'])) {
                    candidates = candidates.filter(f => hasAll(f.label.toLowerCase(), ['code', 'conduct']) || hasAll(f.name.toLowerCase(), ['code', 'conduct']));
                } else if (idL.includes('refund') || titleL.includes('refund') || hasAll(titleL, ['terms', 'conditions'])) {
                    candidates = candidates.filter(f => f.label.toLowerCase().includes('refund') || hasAll(f.label.toLowerCase(), ['terms', 'conditions']) || f.name.toLowerCase().includes('refund'));
                } else if (idL.includes('covid') || titleL.includes('covid')) {
                    candidates = candidates.filter(f => f.label.toLowerCase().includes('covid') || f.name.toLowerCase().includes('covid'));
                } else if (titleL.includes('waiver') || titleL.includes('release') || idL.includes('waiver') || idL.includes('release')) {
                    candidates = candidates.filter(f => f.label.toLowerCase().includes('waiver') || f.label.toLowerCase().includes('release') || f.name.toLowerCase().includes('waiver'));
                }
                if (!candidates.length) candidates = checkboxSchemas;
                let best: { f: typeof checkboxSchemas[number]; s: number } | null = null;
                for (const c of candidates) {
                    const s = score(def.title, c.label) + score(def.id, c.label);
                    if (!best || s > best.s) best = { f: c, s };
                }
                return best?.f?.name || null;
            };
            const bindings: Record<string, string> = {};
            for (const d of defs) {
                const exact = detectedFields.find(n => n.toLowerCase() === d.id.toLowerCase());
                const chosen = exact || pick(d);
                if (chosen) bindings[d.id] = chosen;
            }
            this.waiverIdToField.set(bindings);
            const current = { ...this.waiversAccepted() } as Record<string, boolean>;
            let changed = false;
            for (const [id, field] of Object.entries(bindings)) {
                if (current[id] !== undefined && current[field] === undefined) {
                    current[field] = current[id]; delete current[id]; changed = true;
                }
            }
            if (changed) this.waiversAccepted.set(current);
        } catch { /* ignore binding issues */ }
    }

    private synthesizeDefinitions(labels: string[]): WaiverDefinition[] {
        const synth: WaiverDefinition[] = [];
        const addUnique = (id: string, title: string) => { if (!synth.some(d => d.id === id)) synth.push({ id, title, html: '', required: true, version: '1' }); };
        for (const label of labels) {
            const l = label.toLowerCase();
            if (l.includes('code of conduct')) addUnique('PlayerRegCodeOfConduct', 'Code of Conduct');
            else if (l.includes('refund')) addUnique('PlayerRegRefundTerms', 'Refund Terms and Conditions');
            else if (l.includes('waiver') || l.includes('release')) addUnique('PlayerRegReleaseOfLiability', 'Player Waiver');
            else {
                const title = label
                    .replace(/^i\s+agree\s+(with|to)\s+the\s+/i, '')
                    .replace(/\s*terms?\s+and\s+conditions\s*$/i, '')
                    .trim() || 'Agreement';
                // sanitize id without regex replace to satisfy lint preference for replaceAll
                const id = 'PlayerReg' + title.split(/[^a-z0-9]+/i).join('');
                addUnique(id, title);
            }
        }
        return synth;
    }
}
