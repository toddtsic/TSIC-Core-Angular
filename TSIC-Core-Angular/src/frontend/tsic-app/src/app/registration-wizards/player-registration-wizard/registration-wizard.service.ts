import { Injectable, inject, signal, effect } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
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
    // Design Principle: EACH REGISTRATION OWNS ITS OWN SNAPSHOT OF FORM VALUES.
    // We do NOT merge or unify formValues across multiple registrations for a player.
    // Any edit that creates a new registration stamps values only into that new registration.
    // Prior registrations remain immutable snapshots (unless explicitly edited one-by-one in future flows).
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
    // Map waiver definition id -> schema field name (checkbox) for acceptance
    waiverIdToField = signal<Record<string, string>>({});
    // Acceptance map stored by schema field name to align with formFieldValues keys in preSubmit
    waiversAccepted = signal<Record<string, boolean>>({}); // fieldName -> accepted
    signatureName = signal<string>('');
    signatureRole = signal<'Parent/Guardian' | 'Adult Player' | ''>('');
    // When waiver acceptance checkboxes are present as form fields, we'll detect and hide them from Forms
    // and render them in the dedicated Waivers step instead.
    waiverFieldNames = signal<string[]>([]);
    // Dev-only: capture a normalized snapshot of GET /family/players for the debug panel on Players step
    debugFamilyPlayersResp = signal<any>(null);

    // Effect: whenever players list or waiver definitions change, re-evaluate waiver acceptance rules.
    // This catches scenarios where a new unregistered player is added AFTER waivers were previously accepted
    // for a sibling, forcing re-attestation.
    private readonly waiverSelectionWatcher = effect(() => {
        // read dependencies
        const _players = this.familyPlayers(); // trigger on list change
        const _defs = this.waiverDefinitions(); // trigger after definitions loaded
        if (!_defs || _defs.length === 0) return; // nothing to evaluate yet
        // Recompute acceptance (logic internally no-ops if there are no selections)
        this.recomputeWaiverAcceptanceOnSelectionChange();
    });

    // Forms data per player (dynamic fields later)
    formData = signal<Record<string, any>>({}); // playerId -> { fieldName: value }
    // US Lacrosse number validation status per player
    usLaxStatus = signal<Record<string, { value: string; status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: any }>>({});

    // Server-side validation errors captured from latest preSubmit response (playerId/field/message).
    // Empty array when none; undefined before first preSubmit call.
    private _serverValidationErrors: PreSubmitValidationErrorDto[] | undefined;
    getServerValidationErrors(): PreSubmitValidationErrorDto[] { return this._serverValidationErrors ? [...this._serverValidationErrors] : []; }
    hasServerValidationErrors(): boolean { return !!this._serverValidationErrors?.length; }

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

    /** Seed required waiver acceptance only when ALL selected players are already registered (edit-only scenario).
     * If any new player is being added, do NOT pre-accept to force re-attestation. */
    private seedAcceptedWaiversIfReadOnly(): void {
        try {
            const defs = this.waiverDefinitions();
            if (!defs || defs.length === 0) return;
            // If already have any accepted entries, don't overwrite (user may have interacted in a new flow)
            if (Object.keys(this.waiversAccepted()).length > 0) return;
            const selectedIds = new Set(this.selectedPlayerIds());
            if (selectedIds.size === 0) return;
            const selectedPlayers = this.familyPlayers().filter(p => selectedIds.has(p.playerId));
            const allSelectedRegistered = selectedPlayers.every(p => p.registered);
            if (!allSelectedRegistered) return;
            const accepted: Record<string, boolean> = {};
            for (const d of defs) {
                if (!d.required) continue;
                const field = this.waiverIdToField()[d.id] || d.id;
                accepted[field] = true;
            }
            this.waiversAccepted.set(accepted);
        } catch (e) {
            console.debug('[RegWizard] seedAcceptedWaiversIfReadOnly failed', e);
        }
    }

    /** Recompute waiver acceptance when player selection changes. Clears acceptance and signature
     * when any unregistered player is selected; pre-seeds only when all selected are registered. */
    private recomputeWaiverAcceptanceOnSelectionChange(): void {
        try {
            const defs = this.waiverDefinitions();
            const selectedIds = new Set(this.selectedPlayerIds());
            if (!defs || defs.length === 0 || selectedIds.size === 0) {
                // Nothing to enforce
                return;
            }
            const selectedPlayers = this.familyPlayers().filter(p => selectedIds.has(p.playerId));
            const allSelectedRegistered = selectedPlayers.every(p => p.registered);
            if (allSelectedRegistered) {
                // If user hasn't explicitly interacted with waivers this session, ensure accepted
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
                // Require re-attestation: clear acceptance and signature since new players are being added
                if (Object.keys(this.waiversAccepted()).length > 0) this.waiversAccepted.set({});
                if (this.signatureName()) this.signatureName.set('');
                if (this.signatureRole()) this.signatureRole.set('');
            }
        } catch (e) {
            console.debug('[RegWizard] recomputeWaiverAcceptanceOnSelectionChange failed', e);
        }
    }

    // --- Waiver helpers ---
    setWaiverAccepted(id: string, accepted: boolean): void {
        const map = { ...this.waiversAccepted() };
        // Accept either a definition id or a field name; normalize to field name
        const binding = this.waiverIdToField()[id] || id;
        if (accepted) map[binding] = true; else delete map[binding];
        this.waiversAccepted.set(map);
    }

    isWaiverAccepted(key: string): boolean {
        // Accept either a definition id or a field name
        const field = this.waiverIdToField()[key] || key;
        return !!this.waiversAccepted()[field];
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
                    // Set raw debug payload (dev-only panel reads this)
                    try {
                        this.debugFamilyPlayersResp.set(resp);
                    } catch { /* ignore */ }
                    // If server provided jobRegForm with an explicit constraint type, set it early
                    // so the wizard can decide to show the Eligibility step before fetching /jobs metadata.
                    try {
                        const jrf = (resp?.jobRegForm ?? resp?.JobRegForm);
                        const rawCt = jrf?.constraintType ?? jrf?.ConstraintType ?? jrf?.teamConstraint ?? jrf?.TeamConstraint ?? null;
                        if (typeof rawCt === 'string' && rawCt.trim().length > 0) {
                            this.teamConstraintType.set(rawCt.trim().toUpperCase());
                        }
                    } catch { /* non-fatal */ }
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
                            // Surface top-level membership / association number directly for edit seeding.
                            sportAssnId: r.sportAssnId ?? r.SportAssnId ?? null,
                            // Normalize into client-side formValues from whichever server field exists (preferring visible-only FormFieldValues)
                            formValues: normalizeFormValues(
                                r.formFieldValues || r.FormFieldValues || r.formValues || r.FormValues
                            )
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

                    // Prefill selectedTeams for registered players using prior registrations.
                    // Strategy: collect all assignedTeamIds from priorRegistrations; if one -> store string, if >1 -> store array.
                    const teamMap: Record<string, string | string[]> = { ...this.selectedTeams() };
                    for (const fp of list) {
                        if (!fp.registered) continue; // only prefill for locked/registered
                        const teamIds = fp.priorRegistrations
                            .map(r => r.assignedTeamId)
                            .filter((id: any): id is string => typeof id === 'string' && !!id);
                        if (teamIds.length === 0) continue; // no assignment history
                        // Deduplicate while preserving order (earlier = older registrations)
                        const unique: string[] = [];
                        for (const t of teamIds) if (!unique.includes(t)) unique.push(t);
                        if (unique.length === 1) teamMap[fp.playerId] = unique[0];
                        else if (unique.length > 1) teamMap[fp.playerId] = unique; // multi-assignment history
                    }
                    this.selectedTeams.set(teamMap);
                    // Keep raw debug payload (already set) so dev panel shows full server snapshot including formFields
                    // Ensure job metadata so forms parse soon after
                    this.ensureJobMetadata(jobPath);
                    this.familyPlayersLoading.set(false);
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family players', err);
                    this.familyPlayers.set([]);
                    this.familyUser.set(null);
                    this.regSaverDetails.set(null);
                    this.debugFamilyPlayersResp.set(null);
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
                    // Offer flag for RegSaver
                    try {
                        const offer = (meta as any).offerPlayerRegsaverInsurance ?? (meta as any).OfferPlayerRegsaverInsurance;
                        this._offerPlayerRegSaver = !!offer;
                    } catch { this._offerPlayerRegSaver = false; }
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
                    // Build structured definitions using explicit mapping when present, but only
                    // if there is a matching acceptance checkbox field in PlayerProfileMetadataJson (Option 4).
                    const defs: WaiverDefinition[] = [];
                    const addDef = (id: string, title: string) => {
                        const html = getMetaString(meta, id);
                        if (typeof html === 'string' && html.trim()) {
                            defs.push({ id, title, html, required: true, version: String(html.length) });
                        }
                    };
                    const rawProfileMeta = this.jobProfileMetadataJson();
                    const hasAcceptanceField = (predicate: (labelL: string, nameL: string, f: any) => boolean): boolean => {
                        if (!rawProfileMeta) return false;
                        try {
                            const parsed = JSON.parse(rawProfileMeta);
                            let fields: any[] = [];
                            if (Array.isArray(parsed)) {
                                fields = parsed;
                            } else if (parsed && Array.isArray((parsed as any).fields)) {
                                fields = (parsed as any).fields;
                            }
                            for (const f of fields) {
                                const name = String(f?.name || f?.dbColumn || f?.field || '').toLowerCase();
                                const label = String(f?.label || f?.displayName || f?.display || f?.name || '').toLowerCase();
                                const t = String(f?.type || f?.inputType || '').toLowerCase();
                                const isCheckbox = t.includes('checkbox') || label.startsWith('i agree');
                                if (!isCheckbox) continue;
                                if (predicate(label, name, f)) return true;
                            }
                        } catch { /* ignore malformed profile metadata */ }
                        return false;
                    };
                    // Gate each default waiver by presence of an acceptance checkbox in the profile schema
                    if (hasAcceptanceField((l, n) => l.includes('waiver') || l.includes('release') || n.includes('waiver'))) {
                        addDef('PlayerRegReleaseOfLiability', 'Player Waiver');
                    }
                    if (hasAcceptanceField((l, n) => (l.includes('code') && l.includes('conduct')) || n.includes('codeofconduct'))) {
                        addDef('PlayerRegCodeOfConduct', 'Code of Conduct');
                    }
                    if (hasAcceptanceField((l, n) => l.includes('covid') || n.includes('covid'))) {
                        addDef('PlayerRegCovid19Waiver', 'Covid Waiver');
                    }
                    if (hasAcceptanceField((l, n) => l.includes('refund') || (l.includes('terms') && l.includes('conditions')) || n.includes('refund'))) {
                        addDef('PlayerRegRefundPolicy', 'Refund Terms and Conditions');
                    }
                    // Fallback: include other PlayerReg* blocks only if there is a matching acceptance checkbox field
                    for (const [id, html] of Object.entries(waivers)) {
                        if (defs.some(d => d.id === id)) continue;
                        const lid = id.toLowerCase();
                        let allow = false;
                        if (lid.includes('codeofconduct')) {
                            allow = hasAcceptanceField((l, n) => (l.includes('code') && l.includes('conduct')) || n.includes('codeofconduct'));
                        } else if (lid.includes('refund') || lid.includes('terms')) {
                            allow = hasAcceptanceField((l, n) => l.includes('refund') || (l.includes('terms') && l.includes('conditions')) || n.includes('refund'));
                        } else if (lid.includes('covid')) {
                            allow = hasAcceptanceField((l, n) => l.includes('covid') || n.includes('covid'));
                        } else if (lid.includes('waiver') || lid.includes('release')) {
                            allow = hasAcceptanceField((l, n) => l.includes('waiver') || l.includes('release') || n.includes('waiver'));
                        } else {
                            // Attempt a generic match using the derived friendly title words (conservative: require at least one word match)
                            const title = friendlyWaiverTitle(id).toLowerCase();
                            const words = title.split(/[^a-z0-9]+/i).filter(w => w.length >= 3);
                            if (words.length) {
                                allow = hasAcceptanceField((l, n) => words.some(w => l.includes(w) || n.includes(w)));
                            }
                        }
                        if (allow) {
                            defs.push({ id, title: friendlyWaiverTitle(id), html, required: true, version: String(html.length) });
                        }
                    }
                    this.waiverDefinitions.set(defs);
                    // After we have definitions, seed acceptance for read-only scenarios (edit/prior-registration)
                    this.seedAcceptedWaiversIfReadOnly();
                    this.parseProfileMetadata();
                    // Attempt prefetch of VI object if feature enabled and family user known
                    if (this._offerPlayerRegSaver && this.familyUser()?.familyUserId) {
                        this.fetchVerticalInsureObject(false);
                    }
                },
                error: err => {
                    console.error('[RegWizard] Failed to load job metadata for form parsing', err);
                }
            });
    }
    // RegSaver offer flag (job-level)
    private _offerPlayerRegSaver = false;
    // VerticalInsure offer state; prefer structured object over any/unknown.
    verticalInsureOffer = signal<{ loading: boolean; data: Record<string, unknown> | null; error: string | null }>({ loading: false, data: null, error: null });

    /** Whether the job offers player RegSaver insurance */
    offerPlayerRegSaver(): boolean { return this._offerPlayerRegSaver; }

    /** Fetch VerticalInsure player-object (registration-cancellation) via API */
    fetchVerticalInsureObject(secondChance: boolean): void {
        if (!this._offerPlayerRegSaver) return; // feature off
        const base = this.resolveApiBase();
        const jobPath = this.jobPath();
        const familyUserId = this.familyUser()?.familyUserId;
        if (!jobPath || !familyUserId) return;
        this.verticalInsureOffer.set({ loading: true, data: null, error: null });
        this.http.get<any>(`${base}/verticalInsure/player-object`, { params: { jobPath, familyUserId, secondChance } })
            .subscribe({
                next: obj => {
                    try {
                        // If disabled response (empty client_id) treat as not offered
                        if (!obj?.client_id || !obj?.product_config) {
                            this.verticalInsureOffer.set({ loading: false, data: null, error: null });
                            return;
                        }
                        // If no product configs (empty registration_cancellation), still surface object (user may not have eligible registrations)
                        this.verticalInsureOffer.set({ loading: false, data: obj, error: null });
                    } catch (e: any) {
                        this.verticalInsureOffer.set({ loading: false, data: null, error: String(e?.message || e) });
                    }
                },
                error: err => {
                    this.verticalInsureOffer.set({ loading: false, data: null, error: 'Failed to load insurance offer.' });
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
            // Detect waiver checkbox fields so we can hide them from Forms UI.
            // Prioritize explicit requiredTrue on checkbox; also include label/name heuristics and dbColumn aliases (e.g., BWaiverSigned1/3).
            const detectedWaiverFields: string[] = [];
            const detectedWaiverLabels: string[] = [];
            const containsAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
            // First pass: explicit requiredTrue check on raw metadata objects (ensures accurate capture)
            for (const raw of fields) {
                try {
                    const name = String(raw?.name || raw?.dbColumn || raw?.field || '');
                    if (!name) continue;
                    const type = String(raw?.type || raw?.inputType || '').toLowerCase();
                    const isCheckbox = type.includes('checkbox') || type === 'bool' || type === 'boolean';
                    const validation = raw?.validation || {};
                    const requiredTrue = !!(validation?.requiredTrue === true);
                    if (isCheckbox && requiredTrue) {
                        detectedWaiverFields.push(name);
                        const label = String(raw?.label || raw?.displayName || raw?.display || name);
                        if (label) detectedWaiverLabels.push(label);
                    }
                } catch { /* ignore malformed entries */ }
            }
            // Second pass: heuristic label/name detection for historical data
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
            // Include dbColumn names for waiver-like fields (ensures server property names are injected if schema name differs)
            for (const f of fields) {
                try {
                    const name = String(f?.name || '');
                    const label = String(f?.label || f?.displayName || f?.display || name || '').toLowerCase();
                    const dbCol = typeof f?.dbColumn === 'string' ? String(f.dbColumn) : '';
                    const type = String(f?.type || f?.inputType || '').toLowerCase();
                    const isCheckbox = type.includes('checkbox');
                    const lname = (name || '').toLowerCase();
                    const dbLower = dbCol.toLowerCase();
                    const looksLikeWaiver = isCheckbox && (
                        label.startsWith('i agree') || label.includes('waiver') || label.includes('release') ||
                        containsAll(label, ['code', 'conduct']) || label.includes('refund') || containsAll(label, ['terms', 'conditions'])
                    );
                    if (looksLikeWaiver || lname.includes('waiver') || dbLower.includes('waiver') || dbLower.includes('codeofconduct') || dbLower.includes('refund')) {
                        if (name) detectedWaiverFields.push(name);
                        if (dbCol) detectedWaiverFields.push(dbCol);
                        if (label) detectedWaiverLabels.push(String(f?.label || ''));
                    }
                } catch { /* ignore malformed entries */ }
            }
            // De-duplicate detected fields (case-insensitive, keep first encountered variant)
            {
                const seen = new Set<string>();
                const uniqCI: string[] = [];
                for (const f of detectedWaiverFields) {
                    if (!f) continue;
                    const key = f.toLowerCase();
                    if (seen.has(key)) continue;
                    seen.add(key);
                    uniqCI.push(f);
                }
                detectedWaiverFields.length = 0;
                detectedWaiverFields.push(...uniqCI);
            }
            this.waiverFieldNames.set(detectedWaiverFields);

            // Build bindings from waiver definition ids to schema field names (checkbox acceptances)
            try {
                const defs = this.waiverDefinitions();
                const bindings: Record<string, string> = {};
                const checkboxFields = schemas.filter(s => s.type === 'checkbox');
                const toWords = (s: string) => s.toLowerCase().split(/[^a-z0-9]+/).filter(w => w.length > 2);
                const score = (title: string, label: string) => {
                    const a = new Set(toWords(title));
                    const b = new Set(toWords(label));
                    let m = 0; for (const w of a) if (b.has(w)) m++;
                    return m;
                };
                const pickByHeuristic = (def: WaiverDefinition): string | null => {
                    // Strong keyword mapping first
                    const idL = def.id.toLowerCase();
                    const titleL = def.title.toLowerCase();
                    const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
                    let candidates = checkboxFields;
                    // Filter candidates by thematic keywords
                    if (idL.includes('codeofconduct') || hasAll(titleL, ['code', 'conduct'])) {
                        candidates = candidates.filter(f => hasAll(f.label.toLowerCase(), ['code', 'conduct']) || hasAll(f.name.toLowerCase(), ['code', 'conduct']));
                    } else if (idL.includes('refund') || hasAll(titleL, ['terms', 'conditions']) || titleL.includes('refund')) {
                        candidates = candidates.filter(f => f.label.toLowerCase().includes('refund') || hasAll(f.label.toLowerCase(), ['terms', 'conditions']) || f.name.toLowerCase().includes('refund'));
                    } else if (idL.includes('covid') || titleL.includes('covid')) {
                        candidates = candidates.filter(f => f.label.toLowerCase().includes('covid') || f.name.toLowerCase().includes('covid'));
                    } else if (titleL.includes('waiver') || titleL.includes('release') || idL.includes('waiver') || idL.includes('release')) {
                        candidates = candidates.filter(f => f.label.toLowerCase().includes('waiver') || f.label.toLowerCase().includes('release') || f.name.toLowerCase().includes('waiver'));
                    }
                    if (candidates.length === 0) candidates = checkboxFields;
                    // Score by token overlap with title; break ties by order in schemas
                    let best: { f: PlayerProfileFieldSchema; s: number } | null = null;
                    for (const f of candidates) {
                        const s = score(def.title, f.label) + score(def.id, f.label);
                        if (!best || s > best.s) best = { f, s };
                    }
                    return best?.f?.name || null;
                };
                for (const d of defs) {
                    // If there is an exact field name match in detected list, prefer it
                    const exact = detectedWaiverFields.find(n => n.toLowerCase() === d.id.toLowerCase());
                    const chosen = exact || pickByHeuristic(d);
                    if (chosen) bindings[d.id] = chosen;
                }
                this.waiverIdToField.set(bindings);
                // Migrate any acceptance entries keyed by definition id -> field name
                const current = { ...this.waiversAccepted() } as Record<string, boolean>;
                let changed = false;
                for (const [id, field] of Object.entries(bindings)) {
                    if (current[id] !== undefined && current[field] === undefined) {
                        current[field] = current[id];
                        delete current[id];
                        changed = true;
                    }
                }
                if (changed) this.waiversAccepted.set(current);
                // If still empty and in edit-only scenario, seed acceptance using field names now that mapping exists
                if (Object.keys(this.waiversAccepted()).length === 0) {
                    this.seedAcceptedWaiversIfReadOnly();
                }
            } catch { /* mapping best-effort */ }

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
                        const id = 'PlayerReg' + title.replaceAll(/[^a-z0-9]+/gi, '');
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
            // Seed existing persisted registration form values for registered players using ONLY the most recent prior registration.
            // (Design Principle enforcement: no cross-registration merging.)
            try {
                const players = this.familyPlayers();
                const schemaNameByLower: Record<string, string> = {};
                for (const s of schemas) schemaNameByLower[s.name.toLowerCase()] = s.name;
                for (const p of players) {
                    if (!p.registered || !p.priorRegistrations?.length) continue;
                    const pid = p.playerId;
                    if (!current[pid]) continue;
                    // Pick the most recent registration (last in array) as the source snapshot.
                    const source = p.priorRegistrations[p.priorRegistrations.length - 1];
                    const fv = source?.formValues || {};
                    for (const [rawK, rawV] of Object.entries(fv)) {
                        if (rawV == null || rawV === '') continue;
                        const kLower = rawK.toLowerCase();
                        let targetName = schemaNameByLower[kLower];
                        if (!targetName) {
                            const foundAlias = Object.keys(aliasMapLocal).find(a => a.toLowerCase() === kLower);
                            if (foundAlias) targetName = aliasMapLocal[foundAlias];
                        }
                        if (!targetName) continue;
                        current[pid][targetName] = rawV; // overwrite (single snapshot source)
                    }
                }
            } catch (e) {
                console.debug('[RegWizard] Prior registration single-snapshot seed failed (non-fatal)', e);
            }
            // Precise alias normalization using dbColumn mapping: migrate values from backend property to schema field name
            const alias = this.aliasFieldMap();
            if (alias && Object.keys(alias).length) {
                for (const [, vals] of Object.entries(current)) {
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
                reject(new Error('Missing jobPath or familyUserId'));
                return;
            }
            // Gather selected teams per player
            const teamSelections: PreSubmitTeamSelectionDto[] = [];
            const selectedIds = this.selectedPlayerIds();
            for (const pid of selectedIds) {
                const teamId = this.selectedTeams()[pid];
                if (!teamId) continue;
                // Build visible-only form values for this player
                const formValues = this.buildPreSubmitFormValuesForPlayer(pid);
                if (Array.isArray(teamId)) {
                    for (const tid of teamId) teamSelections.push({ playerId: pid, teamId: tid, formValues });
                } else {
                    teamSelections.push({ playerId: pid, teamId, formValues });
                }
            }
            const payload: PreSubmitRegistrationRequestDto = {
                jobPath,
                familyUserId,
                teamSelections
            };
            firstValueFrom(this.http.post<PreSubmitRegistrationResponseDto & { validationErrors?: PreSubmitValidationErrorDto[] }>(`${base}/registration/preSubmit`, payload))
                .then(resp => {
                    try {
                        // Capture server-side validation errors (metadata enforced). Store for UI consumption.
                        const ve = (resp as any)?.validationErrors as PreSubmitValidationErrorDto[] | undefined;
                        if (ve && Array.isArray(ve) && ve.length) {
                            this._serverValidationErrors = ve;
                        } else {
                            this._serverValidationErrors = [];
                        }
                        const ins: any = (resp as any)?.insurance;
                        if (ins?.available && ins?.playerObject) {
                            this.verticalInsureOffer.set({ loading: false, data: ins.playerObject, error: null });
                        } else if (this._offerPlayerRegSaver && this.familyUser()?.familyUserId) {
                            // Fetch after PreSubmit to include newly created registrations
                            this.fetchVerticalInsureObject(false);
                        }
                    } catch { /* ignore */ }
                    if (resp) resolve(resp);
                    else reject(new Error('No response from preSubmit API'));
                })
                .catch(err => reject(err instanceof Error ? err : new Error(String(err))));
        });
    }

    /** Build a dictionary of visible (non-hidden, non-adminOnly) form fields for a given player, using the parsed field schemas. */
    private buildVisibleFormValuesForPlayer(playerId: string): { [key: string]: Json } {
        const schemas = this.profileFieldSchemas();
        const hidden = new Set(this.waiverFieldNames().map(n => n.toLowerCase()));
        const visibleNames = new Set(
            schemas
                .filter(s => (s.visibility ?? 'public') !== 'hidden' && (s.visibility ?? 'public') !== 'adminOnly')
                .map(s => s.name)
        );
        const all = this.playerFormValues()[playerId] || {} as { [k: string]: any };
        const out: { [k: string]: Json } = {};
        for (const [k, v] of Object.entries(all)) {
            if (!visibleNames.has(k)) continue;
            // Exclude waiver acceptance fields from Forms payload; they will be injected explicitly later
            if (hidden.has(k.toLowerCase())) continue;
            // Allow null/empty values to pass through; skip only undefined
            if (v === undefined) continue;
            out[k] = v as Json;
        }
        return out;
    }

    /**
     * Build form values for preSubmit: visible fields plus waiver checkbox results.
     * Waiver fields are rendered on the Waivers step, so we inject their boolean values here
     * so the server-side metadata validation sees them.
     */
    private buildPreSubmitFormValuesForPlayer(playerId: string): { [key: string]: Json } {
        const out = this.buildVisibleFormValuesForPlayer(playerId);
        const waiverNames = this.waiverFieldNames();
        // Inject actual acceptance state (no unconditional force); preserve design principle (snapshot built from explicit user actions only).
        try {
            if (Array.isArray(waiverNames) && waiverNames.length) {
                for (const name of waiverNames) {
                    // If required waiver accepted, send true; else omit (server will validate).
                    out[name] = this.isWaiverAccepted(name) ? true : false;
                }
            }
        } catch (e) {
            console.warn('[RegWizard] Waiver acceptance injection failed – falling back to explicit map', e);
            if (Array.isArray(waiverNames)) {
                for (const name of waiverNames) out[name] = !!this.isWaiverAccepted(name);
            }
        }
        return out;
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
    private parsedJobOptions: Json | null | undefined;
    private getJobOptionsObject(): Json | null {
        if (this.parsedJobOptions !== undefined) return this.parsedJobOptions;
        const raw = this.jobJsonOptions();
        if (!raw) { this.parsedJobOptions = null; return null; }
        try { this.parsedJobOptions = JSON.parse(raw); }
        catch { this.parsedJobOptions = null; }
        return (this.parsedJobOptions ?? null);
    }
    /** Required valid-through date for USA Lax membership (from Job JsonOptions.USLaxNumberValidThroughDate) */
    getUsLaxValidThroughDate(): Date | null {
        const opts = this.getJobOptionsObject() as { [k: string]: any } | null;
        const v = opts ? (opts['USLaxNumberValidThroughDate'] ?? opts['usLaxNumberValidThroughDate'] ?? null) : null;
        if (!v) return null;
        const d = new Date(v);
        return Number.isNaN(d.getTime()) ? null : d;
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
            if (!Number.isNaN(d.getTime())) return d;
        }
        const vals = this.playerFormValues()[playerId] || {};
        const keys = Object.keys(vals);
        const pick = keys.find(k => k.toLowerCase() === 'dob')
            || keys.find(k => k.toLowerCase().includes('birth') && k.toLowerCase().includes('date'))
            || null;
        if (pick) {
            const d = new Date(vals[pick]);
            if (!Number.isNaN(d.getTime())) return d;
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
        // Re-evaluate waiver acceptance state based on current selection
        this.recomputeWaiverAcceptanceOnSelectionChange();
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

    // --- Client-side metadata driven validation -------------------------------------------
    /** Determine if a schema field is considered the USA Lacrosse number (for special async validation rules). */
    private isUsLaxSchemaField(field: PlayerProfileFieldSchema): boolean {
        const lname = field.name.toLowerCase();
        const llabel = field.label.toLowerCase();
        return lname === 'sportassnid' || llabel.includes('lacrosse');
    }

    /** Visibility logic reused by Forms step; centralizes hidden/admin-only & conditional display rules. */
    private isFieldVisibleForPlayer(playerId: string, field: PlayerProfileFieldSchema): boolean {
        if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
        // Hide waiver fields (rendered in Waivers step)
        if (this.waiverFieldNames().includes(field.name)) return false;
        // Hide legacy team selection fields (handled in Teams step)
        const lname = field.name.toLowerCase();
        const llabel = field.label.toLowerCase();
        if (['team', 'teamid', 'teams'].includes(lname) || llabel.includes('select a team')) return false;
        // Hide generic eligibility markers
        if (lname === 'eligibility' || llabel.includes('eligibility')) return false;
        const tctype = (this.teamConstraintType() || '').toUpperCase();
        const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
        if (tctype === 'BYGRADYEAR') {
            if (hasAll(lname, ['grad', 'year']) || hasAll(llabel, ['grad', 'year'])) return false;
        } else if (tctype === 'BYAGEGROUP') {
            if (hasAll(lname, ['age', 'group']) || hasAll(llabel, ['age', 'group'])) return false;
        } else if (tctype === 'BYAGERANGE') {
            if (hasAll(lname, ['age', 'range']) || hasAll(llabel, ['age', 'range'])) return false;
        }
        if (!field.condition) return true;
        const otherVal = this.getPlayerFieldValue(playerId, field.condition.field);
        const op = (field.condition.operator || 'equals').toLowerCase();
        if (op === 'equals') return otherVal === field.condition.value;
        // Default fallback matches equals semantics
        return otherVal === field.condition.value;
    }

    /** Validate a single field for a player. Returns null if valid, else a message string. */
    private validateFieldForPlayer(playerId: string, field: PlayerProfileFieldSchema): string | null {
        if (!this.isFieldVisibleForPlayer(playerId, field)) return null; // invisible fields ignored
        // Registered (edit/readonly) players are not required to re-validate
        if (this.isPlayerLocked(playerId)) return null;
        const raw = this.getPlayerFieldValue(playerId, field.name);
        const str = raw == null ? '' : String(raw).trim();
        const isEmpty = str.length === 0;

        // Required check (checkbox must be true; text/date/number must be non-empty)
        if (field.required) {
            if (field.type === 'checkbox') {
                if (raw !== true) return 'Required';
            } else if (isEmpty) {
                return 'Required';
            }
        }

        // Type-specific sanity (lightweight)
        if (!isEmpty) {
            if (field.type === 'number') {
                if (Number.isNaN(Number(str))) return 'Must be a number';
            } else if (field.type === 'date') {
                const dt = new Date(str);
                if (Number.isNaN(dt.getTime())) return 'Invalid date';
            } else if (field.type === 'select') {
                if (field.options?.length && !field.options.includes(str)) return 'Invalid option';
            } else if (field.type === 'multiselect') {
                if (Array.isArray(raw)) {
                    const arr = raw;
                    if (field.options?.length && arr.some(v => !field.options.includes(String(v)))) return 'Invalid option';
                    if (field.required && arr.length === 0) return 'Required';
                } else if (field.required) {
                    return 'Required';
                }
            }
        }

        // USA Lacrosse async validator integration: if field is US Lax field and value present, require usLaxStatus valid
        if (this.isUsLaxSchemaField(field)) {
            const statusEntry = this.usLaxStatus()[playerId];
            const status = statusEntry?.status || 'idle';
            if (field.required) {
                if (isEmpty) return 'Required';
                if (status === 'validating') return 'Validating…';
                if (status === 'invalid') return statusEntry?.message || 'Invalid membership';
                if (status !== 'valid') return 'Membership not validated';
            } else if (!isEmpty) {
                if (status === 'validating') return 'Validating…';
                if (status === 'invalid') return statusEntry?.message || 'Invalid membership';
                if (status !== 'valid') return 'Membership not validated';
            }
        }
        return null;
    }

    /** Returns a map of playerId -> fieldName -> error message for currently selected players. */
    validateAllSelectedPlayers(): Record<string, Record<string, string>> {
        const errors: Record<string, Record<string, string>> = {};
        const schemas = this.profileFieldSchemas();
        for (const pid of this.selectedPlayerIds()) {
            for (const field of schemas) {
                const msg = this.validateFieldForPlayer(pid, field);
                if (msg) {
                    if (!errors[pid]) errors[pid] = {};
                    errors[pid][field.name] = msg;
                }
            }
        }
        return errors;
    }

    /** True when all visible required (and entered optional) fields are valid for all selected, non-locked players. */
    areFormsValid(): boolean {
        const schemas = this.profileFieldSchemas();
        for (const pid of this.selectedPlayerIds()) {
            if (this.isPlayerLocked(pid)) continue; // skip readonly players
            for (const field of schemas) {
                const msg = this.validateFieldForPlayer(pid, field);
                if (msg) return false;
            }
        }
        return true;
    }

    /** Per-player validity helper. */
    arePlayerFormsValid(playerId: string): boolean {
        const schemas = this.profileFieldSchemas();
        if (this.isPlayerLocked(playerId)) return true;
        for (const field of schemas) {
            const msg = this.validateFieldForPlayer(playerId, field);
            if (msg) return false;
        }
        return true;
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
    // Only visible fields are sent; keys come from field schema names
    formValues?: { [key: string]: Json };
}
export interface PreSubmitRegistrationResponseDto {
    teamResults: PreSubmitTeamResultDto[];
    nextTab: string;
    insurance?: PreSubmitInsuranceDto;
    validationErrors?: PreSubmitValidationErrorDto[];
}
export interface PreSubmitTeamResultDto {
    playerId: string;
    teamId: string;
    isFull: boolean;
    teamName: string;
    message: string;
    registrationCreated: boolean;
}

export interface PreSubmitInsuranceDto {
    available: boolean;
    playerObject?: Record<string, unknown> | null;
    error?: string | null;
    expiresUtc?: string | null;
    stateId?: string | null;
}

export interface PreSubmitValidationErrorDto {
    playerId: string;
    field: string;
    message: string;
}

// Removed unified registration context types; client now loads players and metadata directly.

// Enriched DTOs moved to ./family-players.dto

// JSON type helper to avoid `any`/`unknown` in public fields
type Json = string | number | boolean | null | Json[] | { [key: string]: Json };

// Helper to create a friendly title from a PlayerReg* key
// e.g., PlayerRegReleaseOfLiability -> Release Of Liability
function friendlyWaiverTitle(key: string): string {
    const trimmed = key.replace(/^PlayerReg/, '');
    return trimmed.replaceAll(/([a-z])([A-Z])/g, '$1 $2').trim();
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
            const rawVal = explicitKey[1];
            let valRaw = '';
            if (typeof rawVal === 'string') {
                valRaw = rawVal.toUpperCase();
            } else if (typeof rawVal === 'number' || typeof rawVal === 'boolean') {
                valRaw = String(rawVal).toUpperCase();
            }
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
