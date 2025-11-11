import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
// Import the default environment, but we'll dynamically prefer the local dev API when running on localhost.
import { environment } from '../../../environments/environment';
import { FamilyPlayer, FamilyPlayerRegistration, RegSaverDetails, normalizeFormValues } from './family-players.dto';

export type PaymentOption = 'PIF' | 'Deposit' | 'ARB';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');


    // Family account presence (from Family Check step)
    hasFamilyAccount = signal<'yes' | 'no' | null>(null);

    // Players and selections
    // Family players enriched with prior registrations + selection flag
    familyPlayers = signal<FamilyPlayer[]>([]);
    familyPlayersLoading = signal<boolean>(false);
    // Family user summary (from players endpoint); name fields used for header badge
    familyUser = signal<{ familyUserId: string; displayName: string; userName: string } | null>(null);
    // RegSaver (optional insurance) details for family/job
    regSaverDetails = signal<RegSaverDetails | null>(null);
    // Whether an existing player registration for the current job + active family user already exists.
    // null = unknown/not yet checked; true/false = definitive.
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
        this.hasFamilyAccount.set(null);
        this.familyPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.eligibilityByPlayer.set({});
        this.selectedTeams.set({});
        this.formData.set({});
        this.paymentOption.set('PIF');
        this.familyUser.set(null);
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
            const selectedIds = new Set(this.selectedPlayerIds());
            const anyRegisteredSelected = this.familyPlayers().some(p => p.registered && selectedIds.has(p.playerId));
            if (!anyRegisteredSelected) return;
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

    /** Loader: returns family user summary + family players (registered + server-selected flag) + optional RegSaver details. */
    loadFamilyPlayers(jobPath: string): void {
        if (!jobPath) return;
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family players', { jobPath, base });
        this.familyPlayersLoading.set(true);
        this.http.get<any>(`${base}/family/players`, { params: { jobPath, debug: '1' } })
            .subscribe({
                next: resp => {
                    // Normalize familyUser
                    const fu = resp?.familyUser || resp?.FamilyUser || null;
                    if (fu) this.familyUser.set({
                        familyUserId: fu.familyUserId ?? fu.FamilyUserId ?? '',
                        displayName: fu.displayName ?? fu.DisplayName ?? '',
                        userName: fu.userName ?? fu.UserName ?? ''
                    });
                    // Normalize regSaver details
                    const rs = resp?.regSaverDetails || resp?.RegSaverDetails || null;
                    if (rs) {
                        this.regSaverDetails.set({
                            policyNumber: rs.policyNumber ?? rs.PolicyNumber ?? '',
                            policyCreateDate: rs.policyCreateDate ?? rs.PolicyCreateDate ?? ''
                        });
                    } else {
                        this.regSaverDetails.set(null);
                    }
                    // Players list (server property now familyPlayers; fallback to players for backward compatibility)
                    const rawPlayers: any[] = resp?.familyPlayers || resp?.FamilyPlayers || resp?.players || resp?.Players || [];
                    const list: FamilyPlayer[] = rawPlayers.map(p => {
                        const prior: any[] = p.priorRegistrations || p.PriorRegistrations || [];
                        const priorRegs: FamilyPlayerRegistration[] = prior.map(r => ({
                            registrationId: r.registrationId ?? r.RegistrationId ?? '',
                            active: !!(r.active ?? r.Active),
                            financials: {
                                feeBase: +(r.financials?.feeBase ?? r.financials?.FeeBase ?? 0),
                                feeProcessing: +(r.financials?.feeProcessing ?? r.financials?.FeeProcessing ?? 0),
                                feeDiscount: +(r.financials?.feeDiscount ?? r.financials?.FeeDiscount ?? 0),
                                feeDonation: +(r.financials?.feeDonation ?? r.financials?.FeeDonation ?? 0),
                                feeLateFee: +(r.financials?.feeLateFee ?? r.financials?.FeeLateFee ?? 0),
                                feeTotal: +(r.financials?.feeTotal ?? r.financials?.FeeTotal ?? 0),
                                owedTotal: +(r.financials?.owedTotal ?? r.financials?.OwedTotal ?? 0),
                                paidTotal: +(r.financials?.paidTotal ?? r.financials?.PaidTotal ?? 0)
                            },
                            assignedTeamId: r.assignedTeamId ?? r.AssignedTeamId ?? null,
                            assignedTeamName: r.assignedTeamName ?? r.AssignedTeamName ?? null,
                            formValues: normalizeFormValues(r.formValues || r.FormValues)
                        }));
                        return {
                            playerId: p.playerId ?? p.PlayerId ?? '',
                            firstName: p.firstName ?? p.FirstName ?? '',
                            lastName: p.lastName ?? p.LastName ?? '',
                            gender: p.gender ?? p.Gender ?? '',
                            dob: p.dob ?? p.Dob ?? undefined,
                            registered: !!(p.registered ?? p.Registered),
                            selected: !!(p.selected ?? p.Selected ?? (p.registered ?? p.Registered)),
                            priorRegistrations: priorRegs
                        } as FamilyPlayer;
                    });
                    this.familyPlayers.set(list);
                    // Ensure job metadata so forms parse soon after
                    this.ensureJobMetadata(jobPath);
                    this.familyPlayersLoading.set(false);
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family players', err);
                    this.familyPlayers.set([]);
                    this.familyUser.set(null);
                    this.regSaverDetails.set(null);
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
                    // Early derive of constraint type (so wizard can decide to skip Eligibility step before component mounts)
                    try {
                        if (!this.teamConstraintType()) {
                            const derived = deriveConstraintTypeFromJsonOptions(meta.jsonOptions);
                            if (derived) {
                                this.teamConstraintType.set(derived);
                            } else {
                                // Explicitly set null (already null) for clarity when logging
                                this.teamConstraintType.set(null);
                            }
                        }
                    } catch (e) {
                        console.debug('[RegWizard] derive constraint type failed (non-fatal)', e);
                    }
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
            const selectedIds = this.selectedPlayerIds();
            const current = { ...this.playerFormValues() };
            for (const pid of selectedIds) {
                if (!current[pid]) current[pid] = {};
                for (const field of schemas) {
                    if (!(field.name in current[pid])) current[pid][field.name] = null;
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
        const selectedIds = new Set(this.selectedPlayerIds());
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

    // Removed unified context loader & snapshot apply; future: implement server-side context if needed.

    /**
     * Pre-submit API call: checks team roster capacity and creates pending registrations before payment.
     * Returns per-team results and next tab to show.
     */
    preSubmitRegistration(): Promise<PreSubmitRegistrationResponseDto> {
        const base = this.resolveApiBase();
        const jobPath = this.jobPath();
        const familyUserId = this.familyUser()?.familyUserId;
        return new Promise<PreSubmitRegistrationResponseDto>((resolve, reject) => {
            if (!jobPath || !familyUserId) {
                reject('Missing jobPath or familyUserId');
                return;
            }
            // Gather selected teams per player
            const teamSelections: PreSubmitTeamSelectionDto[] = [];
            const selectedIds = this.selectedPlayerIds();
            for (const pid of selectedIds) {
                const teamId = this.selectedTeams()[pid];
                if (!teamId) continue;
                if (Array.isArray(teamId)) {
                    for (const tid of teamId) teamSelections.push({ playerId: pid, teamId: tid });
                } else {
                    teamSelections.push({ playerId: pid, teamId });
                }
            }
            const payload: PreSubmitRegistrationRequestDto = {
                jobPath,
                familyUserId,
                teamSelections
            };
            this.http.post<PreSubmitRegistrationResponseDto>(`${base}/registration/preSubmit`, payload)
                .toPromise()
                .then(resp => {
                    if (resp) resolve(resp);
                    else reject('No response from preSubmit API');
                })
                .catch(err => reject(err));
        });
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

    togglePlayerSelection(player: string | { playerId: string; registered?: boolean; selected?: boolean }): void {
        const playerId = typeof player === 'string' ? player : player?.playerId;
        if (!playerId) return;
        const list = this.familyPlayers();
        this.familyPlayers.set(list.map(p => {
            if (p.playerId !== playerId) return p;
            if (p.registered) return p; // locked
            return { ...p, selected: !p.selected };
        }));
    }

    selectedPlayerIds(): string[] {
        return this.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId);
    }

    // Deprecated adapters removed: components now derive directly from familyPlayers/familyUser.

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

    /** Central helper: a player is locked ONLY when editing an existing registration for this job. */
    isPlayerLocked(playerId: string): boolean {
        return this.familyPlayers().some(p => p.playerId === playerId && p.registered);
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

// DTOs for preSubmit
export interface PreSubmitRegistrationRequestDto {
    jobPath: string;
    familyUserId: string;
    teamSelections: PreSubmitTeamSelectionDto[];
}
export interface PreSubmitTeamSelectionDto {
    playerId: string;
    teamId: string;
}
export interface PreSubmitRegistrationResponseDto {
    teamResults: PreSubmitTeamResultDto[];
    nextTab: string;
}
export interface PreSubmitTeamResultDto {
    playerId: string;
    teamId: string;
    isFull: boolean;
    teamName: string;
    message: string;
    registrationCreated: boolean;
}

// Removed unified registration context types; client now loads players and metadata directly.

// Enriched DTOs moved to ./family-players.dto

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

// Attempt to derive constraint type from jsonOptions (stringified JSON) heuristically.
// Returns one of known types or null if no recognizable constraint token present.
function deriveConstraintTypeFromJsonOptions(raw: string | null | undefined): string | null {
    if (!raw || typeof raw !== 'string' || !raw.trim()) return null;
    try {
        const lower = raw.toLowerCase();
        // Parse the JSON object first; only derive constraint if an explicit constraint key exists.
        let obj: any; try { obj = JSON.parse(raw); } catch { obj = null; }
        if (!obj || typeof obj !== 'object') return null;
        const entries = Object.entries(obj);
        // Look for explicit constraint descriptor keys and prefer their values.
        const explicitKey = entries.find(([k]) => {
            const lk = k.toLowerCase();
            return lk === 'constrainttype' || lk === 'teamconstraint' || lk === 'eligibilityconstraint';
        });
        if (explicitKey) {
            const valRaw = String(explicitKey[1] ?? '').toUpperCase();
            switch (valRaw) {
                case 'BYGRADYEAR':
                case 'BYAGEGROUP':
                case 'BYAGERANGE':
                case 'BYCLUBNAME':
                    return valRaw;
            }
            return null; // explicit but unrecognized value -> treat as none
        }
        // No explicit constraint key: do NOT infer from the mere presence of option sets; safest is null.
        // (Previously we inferred from keys like GradYear lists causing unwanted Eligibility step.)
        return null;
    } catch {
        return null;
    }
}
