import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
// Import the default environment, but we'll dynamically prefer the local dev API when running on localhost.
import { environment } from '../../../environments/environment';

export type PaymentOption = 'PIF' | 'Deposit';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');

    // Start mode selection: 'new' (start fresh), 'edit' (edit prior), 'parent' (update/deassign)
    startMode = signal<'new' | 'edit' | 'parent' | null>(null);

    // Family account presence (from Family Check step)
    hasFamilyAccount = signal<'yes' | 'no' | null>(null);

    // Players and selections
    selectedPlayers = signal<Array<{ userId: string; name: string }>>([]);
    // Family players available for registration
    familyPlayers = signal<Array<{ playerId: string; firstName: string; lastName: string; gender: string; dob?: string; registered?: boolean }>>([]);
    // Loading state for family players fetch (used for big spinner in Players step)
    familyPlayersLoading = signal<boolean>(false);
    // Include username explicitly for UI badge (displayName kept for future flexibility)
    activeFamilyUser = signal<{ familyUserId: string; displayName: string; userName: string } | null>(null);
    familyUsers = signal<Array<{ familyUserId: string; displayName: string; userName: string }>>([]);
    // Whether an existing player registration for the current job + active family user already exists.
    // null = unknown/not yet checked; true/false = definitive.
    existingRegistrationAvailable = signal<boolean | null>(null);
    // Eligibility type is job-wide (e.g., BYGRADYEAR), but the selected value is PER PLAYER
    teamConstraintType = signal<string | null>(null); // e.g., BYGRADYEAR
    // Deprecated: legacy single eligibility value (kept for backward compatibility where needed)
    teamConstraintValue = signal<string | null>(null); // e.g., 2027
    // New: per-player eligibility selection map (playerId -> value)
    eligibilityByPlayer = signal<Record<string, string>>({});
    // Team selection per player. Supports single selection (string) or multi (string[])
    selectedTeams = signal<Record<string, string | string[]>>({}); // playerId -> teamId | teamIds
    // Job metadata raw JSON snapshots
    jobProfileMetadataJson = signal<string | null>(null);
    jobJsonOptions = signal<string | null>(null);
    // Parsed field schema derived from PlayerProfileMetadataJson
    profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
    // Map of backend db column/property names (PascalCase) -> schema field name used in UI
    aliasFieldMap = signal<Record<string, string>>({});
    // Per-player form values (fieldName -> value)
    playerFormValues = signal<Record<string, Record<string, any>>>({});
    // Waiver text blocks (extracted from job meta properties like PlayerRegReleaseOfLiability)
    jobWaivers = signal<Record<string, string>>({});
    // Structured waivers for dedicated step
    waiverDefinitions = signal<WaiverDefinition[]>([]);
    waiversAccepted = signal<Record<string, boolean>>({}); // id -> accepted
    signatureName = signal<string>('');
    signatureRole = signal<'Parent/Guardian' | 'Adult Player' | ''>('');
    // When waiver acceptance checkboxes are present as form fields, we'll detect and hide them from Forms
    // and render them in the dedicated Waivers step instead.
    waiverFieldNames = signal<string[]>([]);

    // Forms data per player (dynamic fields later)
    formData = signal<Record<string, any>>({}); // playerId -> { fieldName: value }
    // US Lacrosse number validation status per player
    usLaxStatus = signal<Record<string, { value: string; status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: any }>>({});

    // Payment
    paymentOption = signal<PaymentOption>('PIF');

    reset(): void {
        this.startMode.set(null);
        this.hasFamilyAccount.set(null);
        this.selectedPlayers.set([]);
        this.familyPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.eligibilityByPlayer.set({});
        this.selectedTeams.set({});
        this.formData.set({});
        this.paymentOption.set('PIF');
        this.activeFamilyUser.set(null);
        this.familyUsers.set([]);
        this.existingRegistrationAvailable.set(null);
        this.jobProfileMetadataJson.set(null);
        this.jobJsonOptions.set(null);
        this.profileFieldSchemas.set([]);
        this.playerFormValues.set({});
        this.jobWaivers.set({});
        this.waiverDefinitions.set([]);
        this.waiversAccepted.set({});
        this.signatureName.set('');
        this.signatureRole.set('');
    }

    /** Seed required waiver acceptance for read-only scenarios (edit mode or previously registered players present).
     * Called after waiverDefinitions populated and again after existing registration snapshot applied. */
    private seedAcceptedWaiversIfReadOnly(): void {
        try {
            const defs = this.waiverDefinitions();
            if (!defs || defs.length === 0) return;
            // If already have any accepted entries, don't overwrite (user may have interacted in a new flow)
            if (Object.keys(this.waiversAccepted()).length > 0) return;
            const isEdit = this.startMode() === 'edit';
            const selectedIds = new Set(this.selectedPlayers().map(p => p.userId));
            const anyRegisteredSelected = this.familyPlayers().some(p => p.registered && selectedIds.has(p.playerId));
            if (!(isEdit || anyRegisteredSelected)) return;
            const accepted: Record<string, boolean> = {};
            for (const d of defs) if (d.required) accepted[d.id] = true;
            this.waiversAccepted.set(accepted);
        } catch (e) {
            console.debug('[RegWizard] seedAcceptedWaiversIfReadOnly failed', e);
        }
    }

    // --- Waiver helpers ---
    setWaiverAccepted(id: string, accepted: boolean): void {
        const map = { ...this.waiversAccepted() };
        if (accepted) map[id] = true; else delete map[id];
        this.waiversAccepted.set(map);
    }

    isWaiverAccepted(id: string): boolean {
        return !!this.waiversAccepted()[id];
    }

    allRequiredWaiversAccepted(): boolean {
        const defs = this.waiverDefinitions();
        if (!defs.length) return true; // nothing required
        for (const d of defs) {
            if (d.required && !this.isWaiverAccepted(d.id)) return false;
        }
        return true;
    }

    requireSignature(): boolean {
        // Simple rule: if any required waiver exists, capture signature.
        return this.waiverDefinitions().some(w => w.required);
    }

    loadFamilyUsers(jobPath: string): void {
        if (!jobPath) return;
        // Future: cache by jobPath; for now simple fetch
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family users', { jobPath, base });
        this.http.get<Array<{ familyUserId: string; displayName: string; userName: string }>>(`${base}/family/users`, { params: { jobPath } })
            .subscribe({
                next: users => {
                    this.familyUsers.set(users || []);
                    // Auto-select if exactly one user
                    if (users?.length === 1) {
                        this.activeFamilyUser.set(users[0]);
                    }
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family users', err);
                    this.familyUsers.set([]);
                }
            });
    }

    loadFamilyPlayers(jobPath: string, familyUserId: string): void {
        if (!jobPath || !familyUserId) return;
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family players', { jobPath, familyUserId, base });
        this.familyPlayersLoading.set(true);
        this.http.get<Array<{ playerId: string; firstName: string; lastName: string; gender: string; dob?: string; registered?: boolean }>>(`${base}/family/players`, { params: { jobPath, familyUserId, debug: '1' } })
            .subscribe({
                next: players => {
                    const list = players || [];
                    this.familyPlayers.set(list);
                    // Pre-select already registered players and lock them via selectedPlayers list
                    const preselected = list.filter(p => p.registered).map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
                    this.selectedPlayers.set(preselected);
                    console.log('[RegWizard] Loaded players', { count: list.length, preselected });
                    // Once players loaded, ensure we have job metadata parsed so Forms step can render
                    this.ensureJobMetadata(jobPath);
                    this.familyPlayersLoading.set(false);
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family players', err);
                    this.familyPlayers.set([]);
                    this.familyPlayersLoading.set(false);
                }
            });
    }

    /** Fetch job metadata if not already loaded so we can parse PlayerProfileMetadataJson & JsonOptions. */
    ensureJobMetadata(jobPath: string): void {
        if (!jobPath) return;
        if (this.jobProfileMetadataJson() && this.jobJsonOptions()) return; // already have it
        const base = this.resolveApiBase();
        this.http.get<{ jobId: string; playerProfileMetadataJson?: string | null; jsonOptions?: string | null;[k: string]: any }>(`${base}/jobs/${encodeURIComponent(jobPath)}`)
            .subscribe({
                next: meta => {
                    this.jobId.set(meta.jobId);
                    this.jobProfileMetadataJson.set(meta.playerProfileMetadataJson || null);
                    this.jobJsonOptions.set(meta.jsonOptions || null);
                    // Helper to read values regardless of camelCase vs PascalCase coming from API
                    const getMetaString = (obj: any, key: string): string | null => {
                        const pascal = key;
                        const camel = key.length ? key.charAt(0).toLowerCase() + key.slice(1) : key;
                        const val = obj?.[pascal] ?? obj?.[camel] ?? null;
                        return (typeof val === 'string' && val.trim()) ? String(val).trim() : null;
                    };
                    const normalizeId = (k: string): string => k.length ? (k.charAt(0).toUpperCase() + k.slice(1)) : k;
                    // Extract waiver text blocks heuristically: keys starting with playerreg/PlayerReg and containing long string content
                    const waivers: Record<string, string> = {};
                    for (const [k, v] of Object.entries(meta)) {
                        const lower = k.toLowerCase();
                        if (lower.startsWith('playerreg') && typeof v === 'string' && v.trim().length > 0) {
                            waivers[normalizeId(k)] = v.trim();
                        }
                    }
                    this.jobWaivers.set(waivers);
                    // Build structured definitions using explicit mapping when present
                    const defs: WaiverDefinition[] = [];
                    const addDef = (id: string, title: string) => {
                        const html = getMetaString(meta, id);
                        if (typeof html === 'string' && html.trim()) {
                            defs.push({ id, title, html, required: true, version: String(html.length) });
                        }
                    };
                    addDef('PlayerRegReleaseOfLiability', 'Player Waiver');
                    addDef('PlayerRegCodeOfConduct', 'Code of Conduct');
                    addDef('PlayerRegCovid19Waiver', 'Covid Waiver');
                    addDef('PlayerRegRefundPolicy', 'Refund Terms and Conditions');
                    // Fallback: include any other PlayerReg* blocks not already added
                    for (const [id, html] of Object.entries(waivers)) {
                        if (!defs.some(d => d.id === id)) {
                            defs.push({ id, title: friendlyWaiverTitle(id), html, required: true, version: String(html.length) });
                        }
                    }
                    this.waiverDefinitions.set(defs);
                    // After we have definitions, seed acceptance for read-only scenarios (edit/prior-registration)
                    this.seedAcceptedWaiversIfReadOnly();
                    this.parseProfileMetadata();
                },
                error: err => {
                    console.error('[RegWizard] Failed to load job metadata for form parsing', err);
                }
            });
    }

    /** Schema parsing for PlayerProfileMetadataJson (simplified MVP). */
    private parseProfileMetadata(): void {
        const raw = this.jobProfileMetadataJson();
        if (!raw) {
            this.profileFieldSchemas.set([]);
            this.waiverFieldNames.set([]);
            return;
        }
        try {
            const json = JSON.parse(raw);
            // Expect either an array of field defs or an object with fields array.
            let fields: any[] = [];
            if (Array.isArray(json)) {
                fields = json;
            } else if (json && Array.isArray(json.fields)) {
                fields = json.fields;
            }
            // Parse job-level JsonOptions (option sets) if present so we can override dropdown values.
            let optionSets: Record<string, any> | null = null;
            const rawOpts = this.jobJsonOptions();
            if (rawOpts) {
                try {
                    const parsed = JSON.parse(rawOpts);
                    if (parsed && typeof parsed === 'object') optionSets = parsed;
                } catch (e) {
                    console.warn('[RegWizard] Failed to parse jobJsonOptions for option overrides', e);
                }
            }
            const getOptionSetInsensitive = (key: string): any[] | null => {
                if (!optionSets || !key) return null;
                const found = Object.keys(optionSets).find(k => k.toLowerCase() === key.toLowerCase());
                return found ? optionSets[found] : null;
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
            const schemas: PlayerProfileFieldSchema[] = fields.map(f => {
                const name = String(f.name || f.dbColumn || f.field || '');
                if (!name) return null;
                let label = String(f.label || f.displayName || f.display || f.name || name);
                // Global normalization: strip legacy informational suffix from labels
                const _suffix = /(\s*\(will not be verified at this time\))$/i;
                if (_suffix.test(label)) {
                    label = label.replace(_suffix, '').trim();
                }
                // If metadata provides a dbColumn (typically PascalCase backend property) and it's different from the schema field name,
                // register a precise alias to bridge backend -> UI without guessing.
                const dbCol = typeof f.dbColumn === 'string' ? f.dbColumn : null;
                if (dbCol && dbCol !== name) {
                    aliasMapLocal[dbCol] = name;
                }
                let type = mapFieldType(f.type || f.inputType);
                // Force US Lacrosse number (sportassnid) to text regardless of metadata type/options
                if (name.toLowerCase() === 'sportassnid' || name.toLowerCase() === 'uslax' || label.toLowerCase().includes('lacrosse')) {
                    type = 'text';
                }
                const required = !!(f.required || f?.validation?.required || f?.validation?.requiredTrue);
                const dsKey = String(f.dataSource || f.optionsSource || f.optionSet || '').trim();
                const options = (() => {
                    // Priority 1: explicit field.options (array of primitives or {value,label})
                    const direct = Array.isArray(f.options) ? f.options : [];
                    let mapped = direct.map((o: any) => String(o?.value ?? o?.Value ?? o?.label ?? o?.Text ?? o));
                    // Priority 2: job option set key via dataSource
                    if ((!mapped || mapped.length === 0) && optionSets && dsKey) {
                        const setVal = optionSets[dsKey] ?? getOptionSetInsensitive(dsKey) ?? null;
                        if (Array.isArray(setVal)) {
                            mapped = setVal.map(v => {
                                if (v && typeof v === 'object') {
                                    const val = v.value ?? v.Value ?? v.id ?? v.Id ?? v.code ?? v.Code ?? v.year ?? v.Year ?? v;
                                    return String(val);
                                }
                                return String(v);
                            });
                        }
                    }
                    // Priority 3: explicit label/name -> option set mapping for known apparel/position lists
                    if ((!mapped || mapped.length === 0) && optionSets) {
                        const key = getMappedOptionSetKey(name, label);
                        const setVal = key ? getOptionSetInsensitive(key) : null;
                        if (Array.isArray(setVal)) {
                            mapped = setVal.map(v => (v && typeof v === 'object') ? String(v.value ?? v.Value ?? v) : String(v));
                        }
                    }
                    // Priority 4: option set matching field name (fallback)
                    if ((!mapped || mapped.length === 0) && optionSets && name) {
                        const key = Object.keys(optionSets).find(k => k.toLowerCase() === name.toLowerCase());
                        const setVal = key ? optionSets[key] : null;
                        if (Array.isArray(setVal)) {
                            mapped = setVal.map(v => (v && typeof v === 'object') ? String(v.value ?? v.Value ?? v) : String(v));
                        }
                    }
                    // Priority 5: targeted substring catch-all for Kilt sizes (legacy data)
                    if ((!mapped || mapped.length === 0) && optionSets && (type === 'select' || type === 'multiselect')) {
                        const lname = (label || name).toLowerCase();
                        if (lname.includes('kilt')) {
                            const k = Object.keys(optionSets).find(x => x.toLowerCase().includes('kilt') && (x.toLowerCase().includes('size') || true));
                            const setVal = k ? optionSets[k] : null;
                            if (Array.isArray(setVal)) {
                                mapped = setVal.map(v => (v && typeof v === 'object') ? String(v.value ?? v.Value ?? v) : String(v));
                            }
                            // Dev-only: warn if kilt size options unresolved
                            try {
                                const host = globalThis.location?.host?.toLowerCase?.() ?? '';
                                if ((!mapped || mapped.length === 0) && (host.startsWith('localhost') || host.startsWith('127.0.0.1'))) {
                                    console.warn('[RegWizard] No options resolved for kilt size field', { field: name, label, optionSetKeys: Object.keys(optionSets) });
                                }
                            } catch { /* ignore */ }
                        }
                    }
                    return mapped;
                })();
                const helpText = f.helpText || f.help || f.placeholder || null;
                const visibility = ((): 'public' | 'adminOnly' | 'hidden' | undefined => {
                    const vis = f.visibility || f.adminOnly; // adminOnly legacy
                    if (typeof vis === 'string') {
                        const lower = vis.toLowerCase();
                        if (lower === 'adminonly') return 'adminOnly';
                        if (lower === 'hidden') return 'hidden';
                        return 'public';
                    }
                    if (vis === true) return 'adminOnly';
                    return undefined;
                })();
                const condition = ((): { field: string; value: any; operator?: string } | null => {
                    const c = f.condition;
                    if (c && typeof c === 'object' && c.field) {
                        return { field: String(c.field), value: c.value, operator: c.operator ? String(c.operator) : undefined };
                    }
                    return null;
                })();
                return { name, label, type, required, options, helpText, visibility, condition } as PlayerProfileFieldSchema;
            }).filter(s => !!s?.name) as PlayerProfileFieldSchema[];
            this.profileFieldSchemas.set(schemas);
            this.aliasFieldMap.set(aliasMapLocal);
            // Detect waiver-like checkbox fields so we can hide them from Forms UI
            const detectedWaiverFields: string[] = [];
            const detectedWaiverLabels: string[] = [];
            const containsAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
            for (const field of schemas) {
                const lname = field.name.toLowerCase();
                const llabel = field.label.toLowerCase();
                const isCheckbox = field.type === 'checkbox';
                const looksLikeWaiver = isCheckbox && (
                    llabel.startsWith('i agree') ||
                    llabel.includes('waiver') || llabel.includes('release') ||
                    containsAll(llabel, ['code', 'conduct']) ||
                    llabel.includes('refund') || containsAll(llabel, ['terms', 'conditions'])
                );
                if (looksLikeWaiver || lname.includes('waiver') || lname.includes('codeofconduct') || lname.includes('refund')) {
                    detectedWaiverFields.push(field.name);
                    detectedWaiverLabels.push(field.label);
                }
            }
            this.waiverFieldNames.set(detectedWaiverFields);

            // If no job-level waiver definitions were populated, synthesize minimal ones from detected fields
            if ((this.waiverDefinitions()?.length ?? 0) === 0 && detectedWaiverLabels.length > 0) {
                const synthDefs: WaiverDefinition[] = [];
                const addUnique = (id: string, title: string) => {
                    if (!synthDefs.some(d => d.id === id)) synthDefs.push({ id, title, html: '', required: true, version: '1' });
                };
                for (const label of detectedWaiverLabels) {
                    const l = label.toLowerCase();
                    if (l.includes('code of conduct')) addUnique('PlayerRegCodeOfConduct', 'Code of Conduct');
                    else if (l.includes('refund')) addUnique('PlayerRegRefundTerms', 'Refund Terms and Conditions');
                    else if (l.includes('waiver') || l.includes('release')) addUnique('PlayerRegReleaseOfLiability', 'Player Waiver');
                    else {
                        // Fallback: derive a title from the label by removing common prefixes
                        const title = label
                            .replace(/^i\s+agree\s+(with|to)\s+the\s+/i, '')
                            .replace(/\s*terms?\s+and\s+conditions\s*$/i, '')
                            .trim() || 'Agreement';
                        const id = 'PlayerReg' + title.replace(/[^a-z0-9]+/gi, '');
                        addUnique(id, title);
                    }
                }
                this.waiverDefinitions.set(synthDefs);
            }
            // Initialize per-player form values lazily
            const players = this.selectedPlayers();
            const current = { ...this.playerFormValues() };
            for (const p of players) {
                if (!current[p.userId]) current[p.userId] = {};
                for (const field of schemas) {
                    if (!(field.name in current[p.userId])) current[p.userId][field.name] = null;
                }
            }
            // Precise alias normalization using dbColumn mapping: migrate values from backend property to schema field name
            const alias = this.aliasFieldMap();
            if (alias && Object.keys(alias).length) {
                for (const [pid, vals] of Object.entries(current)) {
                    for (const [from, to] of Object.entries(alias)) {
                        if (from in vals && !(to in vals)) {
                            vals[to] = vals[from];
                        }
                    }
                }
            }
            this.playerFormValues.set(current);
        } catch (err) {
            console.error('[RegWizard] Failed to parse PlayerProfileMetadataJson', err);
            this.profileFieldSchemas.set([]);
        }
    }

    /** Update a single player's field value */
    setPlayerFieldValue(playerId: string, fieldName: string, value: any): void {
        const all = { ...this.playerFormValues() };
        if (!all[playerId]) all[playerId] = {};
        all[playerId][fieldName] = value;
        this.playerFormValues.set(all);
        // Track US Lacrosse number value in usLaxStatus map when field updated
        if (fieldName.toLowerCase() === 'sportassnid') {
            const statusMap = { ...this.usLaxStatus() };
            const existing = statusMap[playerId] || { value: '', status: 'idle' };
            statusMap[playerId] = { ...existing, value: String(value || ''), status: 'idle' };
            this.usLaxStatus.set(statusMap);
        }
    }

    /** Convenience accessor */
    getPlayerFieldValue(playerId: string, fieldName: string): any {
        return this.playerFormValues()[playerId]?.[fieldName];
    }

    /** Remove form values & team selections for players no longer selected (call after deselect) */
    pruneDeselectedPlayers(): void {
        const selectedIds = new Set(this.selectedPlayers().map(p => p.userId));
        const forms = { ...this.playerFormValues() };
        const teams = { ...this.selectedTeams() };
        for (const pid of Object.keys(forms)) {
            if (!selectedIds.has(pid)) delete forms[pid];
        }
        for (const pid of Object.keys(teams)) {
            if (!selectedIds.has(pid)) delete teams[pid];
        }
        this.playerFormValues.set(forms);
        this.selectedTeams.set(teams);
    }

    /** Placeholder: load an existing registration to prefill teams and form values (edit/parent modes). */
    loadExistingRegistration(jobPath: string, familyUserId: string): void {
        if (!jobPath || !familyUserId) return;
        const base = this.resolveApiBase();
        // Minimal implementation: attempt GET existing registration snapshot (endpoint to be finalized)
        // Updated endpoint (controller route is singular 'registration')
        const url = `${base}/registration/existing`;
        this.http.get<{ teams: Record<string, string | string[]>; values: Record<string, Record<string, any>> }>(url, { params: { jobPath, familyUserId } })
            .subscribe({ next: d => this.applyExistingRegistration(d), error: e => this.onExistingRegistrationError(e) });
    }

    private applyExistingRegistration(data: { teams?: Record<string, string | string[]>; values?: Record<string, Record<string, any>> } | null): void {
        if (!data) return;
        // Auto-select any players referenced by the existing registration snapshot that are not yet in selectedPlayers.
        // This covers the rollback regression where previously registered players were not marked with registered=true
        // in the familyPlayers list and thus never added to selectedPlayers, causing their form values to be hidden.
        try {
            const currentSelected = this.selectedPlayers();
            const selectedIdSet = new Set(currentSelected.map(p => p.userId));
            const toEnsureIds = new Set<string>();
            for (const pid of Object.keys(data.teams || {})) toEnsureIds.add(pid);
            for (const pid of Object.keys(data.values || {})) toEnsureIds.add(pid);
            if (toEnsureIds.size) {
                const famPlayers = this.familyPlayers();
                const additions: Array<{ userId: string; name: string }> = [];
                for (const pid of toEnsureIds) {
                    if (selectedIdSet.has(pid)) continue;
                    const fam = famPlayers.find(fp => fp.playerId === pid);
                    if (fam) {
                        additions.push({ userId: pid, name: `${fam.firstName} ${fam.lastName}`.trim() });
                    } else {
                        // Fallback placeholder name when family players not yet loaded or player missing.
                        additions.push({ userId: pid, name: 'Player' });
                    }
                }
                if (additions.length) {
                    this.selectedPlayers.set([...currentSelected, ...additions]);
                    console.debug('[RegWizard] Auto-selected players from existing registration snapshot', { added: additions.map(a => a.userId) });
                }
            }
        } catch (e) {
            console.warn('[RegWizard] Failed auto-selecting players from existing registration snapshot', e);
        }
        const schemas = this.profileFieldSchemas() || [];
        const validFields = new Set(schemas.map(s => s.name));
        const schemaGradField = (schemas.find(f => {
            const n = f.name?.toLowerCase?.() || '';
            return n === 'gradyear' || n === 'graduationyear';
        })?.name) || null;
        if (data.teams) {
            const allTeams: Record<string, string | string[]> = {};
            for (const [pid, val] of Object.entries(data.teams)) {
                allTeams[pid] = val;
            }
            this.selectedTeams.set(allTeams);
        }
        if (data.values) {
            const current = { ...this.playerFormValues() } as Record<string, Record<string, any>>;
            const eligMap = { ...this.eligibilityByPlayer() } as Record<string, string>;
            const alias = this.aliasFieldMap();
            for (const [pid, fieldMap] of Object.entries(data.values)) {
                const existing = current[pid] ? { ...current[pid] } : {} as Record<string, any>;
                for (const [k, v] of Object.entries(fieldMap || {})) {
                    const kLower = String(k).toLowerCase();
                    if (validFields.size === 0 || validFields.has(k)) {
                        existing[k] = v;
                        continue;
                    }
                    // Precise alias mapping via dbColumn -> schema field
                    const aliasTarget = alias?.[k];
                    if (aliasTarget && validFields.has(aliasTarget)) {
                        existing[aliasTarget] = v;
                        continue;
                    }
                    // Case-insensitive bridge: map PascalCase or different-cased property to schema canonical name
                    // (Retained as fallback but not preferred; can be removed if undesired)
                    const ciMatch = Array.from(validFields).find(fn => fn.toLowerCase() === kLower);
                    if (ciMatch && !Object.prototype.hasOwnProperty.call(existing, ciMatch)) { existing[ciMatch] = v; continue; }
                    // Bridge: map incoming entity GradYear to schema field GraduationYear (or GradYear) if schema uses a different name
                    if (kLower === 'gradyear' && schemaGradField && validFields.has(schemaGradField)) {
                        existing[schemaGradField] = v;
                        continue;
                    }
                }
                current[pid] = existing;
                // If US Lax number present, seed status map value for consistency
                const usKey = Object.keys(existing).find(n => n.toLowerCase() === 'sportassnid');
                if (usKey) {
                    const statusMap = { ...this.usLaxStatus() };
                    const prev = statusMap[pid] || { value: '', status: 'idle' };
                    statusMap[pid] = { ...prev, value: String(existing[usKey] ?? ''), status: 'idle' };
                    this.usLaxStatus.set(statusMap);
                }
                // Deterministic: only honor canonical key from backend for BYGRADYEAR
                // Derive GradYear regardless of current constraint type to avoid sequencing races
                if (!eligMap[pid]) {
                    const keys = Object.keys(existing);
                    const findKey = (target: string) => keys.find(k => k.toLowerCase() === target);
                    const canonical = findKey('gradyear') || (schemaGradField ? findKey(schemaGradField.toLowerCase()) : undefined) || undefined;
                    if (canonical) {
                        const rawVal = existing[canonical];
                        const valStr = (rawVal ?? '').toString().trim();
                        if (/^(20|19)\d{2}$/.test(valStr)) {
                            eligMap[pid] = valStr;
                            console.debug('[RegWizard] Using GradYear from values', { playerId: pid, field: canonical, value: valStr });
                        } else {
                            console.debug('[RegWizard] GradYear present but invalid', { playerId: pid, field: canonical, rawVal });
                        }
                    } else {
                        console.debug('[RegWizard] GradYear key not present in values', { playerId: pid, keys });
                    }
                }
            }
            this.playerFormValues.set(current);
            if (Object.keys(eligMap).length) {
                this.eligibilityByPlayer.set(eligMap);
                // If all elig values identical, seed legacy teamConstraintValue
                const distinct = Array.from(new Set(Object.values(eligMap)));
                if (distinct.length === 1) {
                    this.teamConstraintValue.set(distinct[0]);
                }
            }
        }
        console.debug('[RegWizard] Prefilled existing registration snapshot');
        // After existing registration applied, we may now know which players are registered; seed waiver acceptance if needed.
        this.seedAcceptedWaiversIfReadOnly();
    }

    private onExistingRegistrationError(err: any): void {
        console.debug('[RegWizard] No existing registration snapshot available', err?.status);
    }

    // Prefer localhost API when running locally regardless of production flag mismatch.
    private resolveApiBase(): string {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost') || host.startsWith('127.0.0.1')) {
                // Hard-coded local API port used by backend.
                return 'https://localhost:7215/api';
            }
        } catch { /* SSR or no window */ }
        return environment.apiUrl.endsWith('/api') ? environment.apiUrl : `${environment.apiUrl}/api`;
    }
    private parsedJobOptions: any | undefined;
    private getJobOptionsObject(): any | null {
        if (this.parsedJobOptions !== undefined) return this.parsedJobOptions;
        const raw = this.jobJsonOptions();
        if (!raw) { this.parsedJobOptions = null; return null; }
        try { this.parsedJobOptions = JSON.parse(raw); }
        catch { this.parsedJobOptions = null; }
        return this.parsedJobOptions;
    }
    /** Required valid-through date for USA Lax membership (from Job JsonOptions.USLaxNumberValidThroughDate) */
    getUsLaxValidThroughDate(): Date | null {
        const opts = this.getJobOptionsObject();
        const v = opts?.USLaxNumberValidThroughDate ?? opts?.usLaxNumberValidThroughDate ?? null;
        if (!v) return null;
        const d = new Date(v);
        return isNaN(d.getTime()) ? null : d;
    }
    /** Last name for a player: prefer familyPlayers list; fallback to form values */
    getPlayerLastName(playerId: string): string | null {
        const fam = this.familyPlayers().find(p => p.playerId === playerId);
        if (fam?.lastName) return fam.lastName;
        const vals = this.playerFormValues()[playerId] || {};
        const keys = Object.keys(vals);
        const pick = keys.find(k => k.toLowerCase() === 'lastname')
            || keys.find(k => k.toLowerCase().includes('last') && k.toLowerCase().includes('name'))
            || null;
        return pick ? String(vals[pick] ?? '').trim() || null : null;
    }
    /** DOB for a player: prefer familyPlayers list; fallback to form values */
    getPlayerDob(playerId: string): Date | null {
        const fam = this.familyPlayers().find(p => p.playerId === playerId);
        if (fam?.dob) {
            const d = new Date(fam.dob);
            if (!isNaN(d.getTime())) return d;
        }
        const vals = this.playerFormValues()[playerId] || {};
        const keys = Object.keys(vals);
        const pick = keys.find(k => k.toLowerCase() === 'dob')
            || keys.find(k => k.toLowerCase().includes('birth') && k.toLowerCase().includes('date'))
            || null;
        if (pick) {
            const d = new Date(vals[pick]);
            if (!isNaN(d.getTime())) return d;
        }
        return null;
    }

    togglePlayerSelection(player: { playerId: string; firstName: string; lastName: string; registered?: boolean }): void {
        if (player.registered) return; // cannot deselect previously registered players
        const current = this.selectedPlayers();
        const id = player.playerId;
        const exists = current.some(p => p.userId === id);
        if (exists) {
            this.selectedPlayers.set(current.filter(p => p.userId !== id));
            // drop eligibility and team assignment for deselected player
            const elig = { ...this.eligibilityByPlayer() };
            delete elig[id];
            this.eligibilityByPlayer.set(elig);
            const teams = { ...this.selectedTeams() };
            delete teams[id];
            this.selectedTeams.set(teams);
        } else {
            this.selectedPlayers.set([...current, { userId: id, name: `${player.firstName} ${player.lastName}`.trim() }]);
        }
    }

    setEligibilityForPlayer(playerId: string, value: string | null | undefined): void {
        const map = { ...this.eligibilityByPlayer() };
        if (value == null || value === '') {
            delete map[playerId];
        } else {
            map[playerId] = String(value);
        }
        this.eligibilityByPlayer.set(map);
    }

    getEligibilityForPlayer(playerId: string): string | undefined {
        return this.eligibilityByPlayer()[playerId];
    }

    setUsLaxValidating(playerId: string): void {
        const m = { ...this.usLaxStatus() }; const cur = m[playerId] || { value: '', status: 'idle' };
        m[playerId] = { ...cur, status: 'validating', message: undefined }; this.usLaxStatus.set(m);
    }
    setUsLaxResult(playerId: string, ok: boolean, message?: string, membership?: any): void {
        const m = { ...this.usLaxStatus() }; const cur = m[playerId] || { value: '', status: 'idle' };
        m[playerId] = { ...cur, status: ok ? 'valid' : 'invalid', message, membership }; this.usLaxStatus.set(m);
    }
}

// --- Field schema types ---
export interface PlayerProfileFieldSchema {
    name: string;
    label: string;
    type: 'text' | 'number' | 'date' | 'select' | 'multiselect' | 'checkbox';
    required: boolean;
    options: string[];
    helpText: string | null;
    visibility?: 'public' | 'adminOnly' | 'hidden';
    condition?: { field: string; value: any; operator?: string } | null;
}

export interface WaiverDefinition {
    id: string;          // source key, e.g., PlayerRegReleaseOfLiability
    title: string;       // display title
    html: string;        // HTML encoded waiver text
    required: boolean;   // whether acceptance is required
    version: string;     // simple version token to force re-consent when content changes
}

// Helper to create a friendly title from a PlayerReg* key
// e.g., PlayerRegReleaseOfLiability -> Release Of Liability
function friendlyWaiverTitle(key: string): string {
    const trimmed = key.replace(/^PlayerReg/, '');
    return trimmed.replace(/([a-z])([A-Z])/g, '$1 $2').trim();
}

function mapFieldType(raw: any): PlayerProfileFieldSchema['type'] {
    const r = String(raw || '').toLowerCase();
    switch (r) {
        case 'text':
        case 'string': return 'text';
        case 'int':
        case 'integer':
        case 'number': return 'number';
        case 'date':
        case 'datetime': return 'date';
        case 'select':
        case 'dropdown': return 'select';
        case 'multiselect':
        case 'multi-select': return 'multiselect';
        case 'checkbox':
        case 'bool':
        case 'boolean': return 'checkbox';
        default: return 'text';
    }
}
