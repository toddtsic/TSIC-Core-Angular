import { Injectable, inject, signal } from '@angular/core';
import { PlayerStateService } from './services/player-state.service';
import { WaiverStateService } from './services/waiver-state.service';
import { FormSchemaService } from './services/form-schema.service';
import type { Loadable } from '../../core/models/state.models';
import type { VIPlayerObjectResponse } from '../../core/api/models/VIPlayerObjectResponse';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
// Import the default environment, but we'll dynamically prefer the local dev API when running on localhost.
import { environment } from '../../../environments/environment';
import { FamilyPlayer, FamilyPlayerRegistration, RegSaverDetails, normalizeFormValues } from './family-players.dto';
import type { PreSubmitRegistrationRequestDto } from '../../core/api/models/PreSubmitRegistrationRequestDto';
import type { PreSubmitRegistrationResponseDto } from '../../core/api/models/PreSubmitRegistrationResponseDto';
import type { PreSubmitTeamSelectionDto } from '../../core/api/models/PreSubmitTeamSelectionDto';
import type { PreSubmitValidationErrorDto } from '../../core/api/models/PreSubmitValidationErrorDto';
import type { FamilyPlayersResponseDto } from '../../core/api/models/FamilyPlayersResponseDto';

export type PaymentOption = 'PIF' | 'Deposit' | 'ARB';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    private readonly playerState = inject(PlayerStateService);
    // Delegated services (extracted logic)
    private readonly waiverState = inject(WaiverStateService);
    private readonly formSchema = inject(FormSchemaService);
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');


    // Family account presence (from Family Check step)
    hasFamilyAccount = signal<'yes' | 'no' | null>(null);

    // Players and selections
    // Family players enriched with prior registrations + selection flag
    familyPlayers = signal<FamilyPlayer[]>([]);
    familyPlayersLoading = signal<boolean>(false);
    // Family user summary (from players endpoint). Includes optional contact fields for convenience defaults on Payment.
    familyUser = signal<{
        familyUserId: string;
        displayName: string;
        userName: string;
        // Optional contact fields (when provided by API)
        firstName?: string;
        lastName?: string;
        address?: string;      // consolidated address
        address1?: string;     // line 1
        address2?: string;     // line 2
        city?: string;
        state?: string;
        zipCode?: string;      // preferred zip property
        zip?: string;          // fallback zip property
        postalCode?: string;   // alternative naming
        // Server-provided credit card info (authoritative guardian billing details)
        ccInfo?: {
            firstName?: string;
            lastName?: string;
            streetAddress?: string;
            zip?: string;
        };
    } | null>(null);
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
    // Backward-compatible facade methods for selectedTeams
    selectedTeams(): Record<string, string | string[]> { return this.playerState.selectedTeams(); }
    setSelectedTeams(map: Record<string, string | string[]>): void { this.playerState.setSelectedTeams(map); }
    // Eligibility facades
    eligibilityByPlayer(): Record<string, string> { return this.playerState.eligibilityByPlayer(); }
    setEligibilityForPlayer(playerId: string, value: string | null | undefined): void { this.playerState.setEligibilityForPlayer(playerId, value); }
    getEligibilityForPlayer(playerId: string): string | undefined { return this.playerState.getEligibilityForPlayer(playerId); }
    // (migrated) eligibility & selectedTeams now owned by PlayerStateService
    // Job metadata raw JSON snapshots
    jobProfileMetadataJson = signal<string | null>(null);
    jobJsonOptions = signal<string | null>(null);
    // Job payment feature flags
    jobHasActiveDiscountCodes = signal<boolean>(false);
    jobUsesAmex = signal<boolean>(false);
    // Payment flags & ARB schedule (ALLOWPIF removed; UI derives options from scenarios only)
    adnArb = signal<boolean>(false);
    adnArbBillingOccurences = signal<number | null>(null);
    adnArbIntervalLength = signal<number | null>(null);
    adnArbStartDate = signal<string | null>(null);
    // Parsed field schema derived from PlayerProfileMetadataJson
    profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
    // Map of backend db column/property names (PascalCase) -> schema field name used in UI
    aliasFieldMap = signal<Record<string, string>>({});
    // Per-player form values (fieldName -> value)
    playerFormValues = signal<Record<string, Record<string, any>>>({});
    // Waiver text HTML blocks (job-level). Structured waiver state lives in WaiverStateService.
    jobWaivers = signal<Record<string, string>>({});
    // Waiver delegating accessors (maintain existing call sites that invoke like signals)
    waiverDefinitions(): WaiverDefinition[] { return this.waiverState.waiverDefinitions(); }
    waiverIdToField(): Record<string, string> { return this.waiverState.waiverIdToField(); }
    waiversAccepted(): Record<string, boolean> { return this.waiverState.waiversAccepted(); }
    waiverFieldNames(): string[] { return this.waiverState.waiverFieldNames(); }
    waiversGateOk(): boolean { return this.waiverState.waiversGateOk(); }
    setWaiversGateOk(v: boolean): void { this.waiverState.waiversGateOk.set(v); }
    signatureName(): string { return this.waiverState.signatureName(); }
    setSignatureName(v: string): void { this.waiverState.signatureName.set(v); }
    signatureRole(): 'Parent/Guardian' | 'Adult Player' | '' { return this.waiverState.signatureRole(); }
    setSignatureRole(v: 'Parent/Guardian' | 'Adult Player' | ''): void { this.waiverState.signatureRole.set(v); }
    // Dev-only: capture a normalized snapshot of GET /family/players for the debug panel on Players step
    debugFamilyPlayersResp = signal<any>(null);

    // Waiver selection/reactivity handled inside WaiverStateService; no local effect needed.

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
    // Last payment summary for Confirmation step
    lastPayment = signal<{
        option: PaymentOption;
        amount: number;
        transactionId?: string;
        subscriptionId?: string;
        viPolicyNumber?: string | null;
        viPolicyCreateDate?: string | null;
        message?: string | null;
    } | null>(null);

    reset(): void {
        this.hasFamilyAccount.set(null);
        this.familyPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.formData.set({});
        this.paymentOption.set('PIF');
        this.lastPayment.set(null);
        this.familyUser.set(null);
        this.jobProfileMetadataJson.set(null);
        this.jobJsonOptions.set(null);
        this.profileFieldSchemas.set([]);
        this.playerFormValues.set({});
        this.jobWaivers.set({});
        // Waiver state reset handled by WaiverStateService automatically (no local reset needed)
        this.adnArb.set(false);
        this.adnArbBillingOccurences.set(null);
        this.adnArbIntervalLength.set(null);
        this.adnArbStartDate.set(null);
        this.jobHasActiveDiscountCodes.set(false);
        this.jobUsesAmex.set(false);
    }

    /** Seed required waiver acceptance only when ALL selected players are already registered (delegated). */
    // Waiver acceptance seeding delegated to WaiverStateService

    // Recompute waiver acceptance via WaiverStateService (after selection changes)
    private recomputeWaiverAcceptanceOnSelectionChange(): void {
        try {
            this.waiverState.recomputeWaiverAcceptanceOnSelectionChange(this.selectedPlayerIds(), this.familyPlayers());
        } catch { /* ignore */ }
    }

    // --- Waiver helpers ---
    setWaiverAccepted(idOrField: string, accepted: boolean): void { this.waiverState.setWaiverAccepted(idOrField, accepted); }

    isWaiverAccepted(key: string): boolean { return this.waiverState.isWaiverAccepted(key); }

    allRequiredWaiversAccepted(): boolean { return this.waiverState.allRequiredWaiversAccepted(); }

    requireSignature(): boolean { return this.waiverState.requireSignature(); }

    /** Loader: returns family user summary + family players (registered + server-selected flag) + optional RegSaver details. */
    loadFamilyPlayers(jobPath: string): void {
        if (!jobPath) return;
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family players', { jobPath, base });
        this.familyPlayersLoading.set(true);
        this.http.get<FamilyPlayersResponseDto>(`${base}/family/players`, { params: { jobPath, debug: '1' } })
            .subscribe({
                next: resp => {
                    this.debugFamilyPlayersResp.set(resp);
                    this.extractConstraintType(resp);
                    this.applyFamilyUser(resp);
                    this.applyRegSaverDetails(resp);
                    const players = this.buildFamilyPlayersList(resp);
                    this.familyPlayers.set(players);
                    this.prefillTeamsFromPriorRegistrations(players);
                    this.ensureJobMetadata(jobPath);
                    this.applyPaymentFlags(resp);
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

    private extractConstraintType(resp: FamilyPlayersResponseDto): void {
        try {
            const jrf = resp.jobRegForm || (resp as any).JobRegForm;
            const rawCt = jrf?.constraintType ?? jrf?.ConstraintType ?? jrf?.teamConstraint ?? jrf?.TeamConstraint ?? null;
            if (typeof rawCt === 'string' && rawCt.trim()) {
                const norm = rawCt.trim().toUpperCase();
                this.teamConstraintType.set(norm);
                try { console.debug('[RegWizard] constraintType from /family/players:', norm); } catch { /* no-op */ }
            } else {
                try { console.warn('[RegWizard] constraintType not present in /family/players response; Eligibility step may be hidden.'); } catch { /* no-op */ }
            }
        } catch { /* ignore */ }
    }

    private applyFamilyUser(resp: FamilyPlayersResponseDto): void {
        const fu = resp.familyUser || (resp as any).FamilyUser || null;
        if (!fu) { this.familyUser.set(null); return; }
        const pick = (o: any, keys: string[]): string | undefined => {
            for (const k of keys) {
                const v = o?.[k];
                if (typeof v === 'string' && v.trim()) return String(v).trim();
            }
            return undefined;
        };
        const norm: any = {
            familyUserId: fu.familyUserId ?? fu.FamilyUserId ?? '',
            displayName: fu.displayName ?? fu.DisplayName ?? '',
            userName: fu.userName ?? fu.UserName ?? '',
            firstName: pick(fu, ['firstName', 'FirstName', 'parentFirstName', 'ParentFirstName', 'motherFirstName', 'MotherFirstName', 'guardianFirstName', 'GuardianFirstName', 'billingFirstName', 'BillingFirstName']),
            lastName: pick(fu, ['lastName', 'LastName', 'parentLastName', 'ParentLastName', 'motherLastName', 'MotherLastName', 'guardianLastName', 'GuardianLastName', 'billingLastName', 'BillingLastName']),
            address: pick(fu, ['address', 'Address', 'billingAddress', 'BillingAddress', 'street', 'Street', 'street1', 'Street1', 'address1', 'Address1']),
            address1: pick(fu, ['address1', 'Address1', 'street1', 'Street1']),
            address2: pick(fu, ['address2', 'Address2', 'street2', 'Street2', 'apt', 'Apt', 'aptNumber', 'AptNumber', 'suite', 'Suite']),
            city: pick(fu, ['city', 'City']),
            state: pick(fu, ['state', 'State', 'stateCode', 'StateCode']),
            zipCode: pick(fu, ['zipCode', 'ZipCode', 'zip', 'Zip', 'postalCode', 'PostalCode']),
            zip: pick(fu, ['zip', 'Zip']),
            postalCode: pick(fu, ['postalCode', 'PostalCode'])
        };
        const rawCc = resp.ccInfo || (resp as any).CcInfo || null;
        if (rawCc) {
            norm.ccInfo = {
                firstName: pick(rawCc, ['firstName', 'FirstName']),
                lastName: pick(rawCc, ['lastName', 'LastName']),
                streetAddress: pick(rawCc, ['streetAddress', 'StreetAddress', 'address', 'Address']),
                zip: pick(rawCc, ['zip', 'Zip', 'zipCode', 'ZipCode', 'postalCode', 'PostalCode'])
            };
        }
        this.familyUser.set(norm);
    }

    private applyRegSaverDetails(resp: FamilyPlayersResponseDto): void {
        const rs = resp.regSaverDetails || (resp as any).RegSaverDetails || null;
        if (!rs) { this.regSaverDetails.set(null); return; }
        this.regSaverDetails.set({
            policyNumber: rs.policyNumber ?? rs.PolicyNumber ?? '',
            policyCreateDate: rs.policyCreateDate ?? rs.PolicyCreateDate ?? ''
        });
    }

    private buildFamilyPlayersList(resp: FamilyPlayersResponseDto): FamilyPlayer[] {
        const rawPlayers: any[] = resp.familyPlayers || (resp as any).FamilyPlayers || (resp as any).players || (resp as any).Players || [];
        return rawPlayers.map(p => {
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
                sportAssnId: r.sportAssnId ?? r.SportAssnId ?? null,
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
    }

    private prefillTeamsFromPriorRegistrations(players: FamilyPlayer[]): void {
        const teamMap: Record<string, string | string[]> = { ...this.playerState.selectedTeams() };
        for (const fp of players) {
            if (!fp.registered) continue;
            const teamIds = fp.priorRegistrations
                .map(r => r.assignedTeamId)
                .filter((id: any): id is string => typeof id === 'string' && !!id);
            if (!teamIds.length) continue;
            const unique: string[] = [];
            for (const t of teamIds) if (!unique.includes(t)) unique.push(t);
            if (unique.length === 1) teamMap[fp.playerId] = unique[0];
            else if (unique.length > 1) teamMap[fp.playerId] = unique;
        }
        this.playerState.setSelectedTeams(teamMap);
    }

    private applyPaymentFlags(resp: FamilyPlayersResponseDto): void {
        try {
            this.jobHasActiveDiscountCodes.set(!!resp.jobHasActiveDiscountCodes);
            this.jobUsesAmex.set(!!resp.jobUsesAmex);
        } catch { /* ignore */ }
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
                    // Payment flags & schedule from server metadata
                    try {
                        const arb = (meta as any).AdnArb ?? (meta as any).adnArb ?? false;
                        const occ = (meta as any).AdnArbBillingOccurences ?? (meta as any).adnArbBillingOccurences ?? null;
                        const intLen = (meta as any).AdnArbIntervalLength ?? (meta as any).adnArbIntervalLength ?? null;
                        const start = (meta as any).AdnArbStartDate ?? (meta as any).adnArbStartDate ?? null;
                        this.adnArb.set(!!arb);
                        this.adnArbBillingOccurences.set(typeof occ === 'number' ? occ : null);
                        this.adnArbIntervalLength.set(typeof intLen === 'number' ? intLen : null);
                        this.adnArbStartDate.set(start ? String(start) : null);
                        // Default to ARB when enabled; else PIF. UI will adjust based on scenario when teams are selected.
                        this.paymentOption.set(this.adnArb() ? 'ARB' : 'PIF');
                    } catch { /* non-critical */ }
                    // Do not set constraintType from client-side heuristics; rely solely on /family/players response.
                    // Offer flag for RegSaver
                    try {
                        const offer = (meta as any).offerPlayerRegsaverInsurance ?? (meta as any).OfferPlayerRegsaverInsurance;
                        this._offerPlayerRegSaver = !!offer;
                    } catch { this._offerPlayerRegSaver = false; }
                    // Do not infer Eligibility constraint type on the client. Rely solely on server-provided jobRegForm.constraintType from /family/players.
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
                            } else if (parsed && Array.isArray(parsed.fields)) {
                                fields = parsed.fields;
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
                    // Delegate waiver definitions to WaiverStateService
                    this.waiverState.setDefinitions(defs);
                    this.waiverState.seedAcceptedWaiversIfReadOnly(this.selectedPlayerIds(), this.familyPlayers());
                    this.parseProfileMetadata();
                },
                error: err => {
                    console.error('[RegWizard] Failed to load job metadata for form parsing', err);
                }
            });
    }
    // RegSaver offer flag (job-level)
    private _offerPlayerRegSaver = false;
    // VerticalInsure offer state retained (widget/playerObject payload) for preSubmit response integration
    verticalInsureOffer = signal<Loadable<VIPlayerObjectResponse>>({ loading: false, data: null, error: null });
    /** Whether the job offers player RegSaver insurance */
    offerPlayerRegSaver(): boolean { return this._offerPlayerRegSaver; }

    // Removed direct fetch of VerticalInsure player-object; preSubmit response is now the single source of truth.
    /** Delegated schema + waiver processing via extracted services. */
    private parseProfileMetadata(): void {
        const rawMeta = this.jobProfileMetadataJson();
        const rawOpts = this.jobJsonOptions();
        this.formSchema.parse(rawMeta, rawOpts);
        const schemas = this.formSchema.profileFieldSchemas();
        this.profileFieldSchemas.set(schemas);
        this.aliasFieldMap.set(this.formSchema.aliasFieldMap());
        this.bindWaiversToSchemas(schemas);
        this.initializeFormValuesForSelectedPlayers(schemas);
        this.seedPlayerValuesFromPriorRegistrations(schemas);
        this.applyAliasBackfill();
        this.seedEligibilityFromSchemas(schemas);
    }

    private bindWaiversToSchemas(schemas: PlayerProfileFieldSchema[]): void {
        this.waiverState.processSchemasAndBindWaivers(
            this.waiverState.waiverDefinitions(),
            schemas.map(s => ({ name: s.name, label: s.label, type: s.type, required: s.required, visibility: s.visibility })),
            this.selectedPlayerIds(),
            this.familyPlayers()
        );
    }

    private initializeFormValuesForSelectedPlayers(schemas: PlayerProfileFieldSchema[]): void {
        const selectedIds = this.selectedPlayerIds();
        const current = { ...this.playerFormValues() } as Record<string, Record<string, any>>;
        for (const pid of selectedIds) {
            if (!current[pid]) current[pid] = {};
            for (const f of schemas) if (!(f.name in current[pid])) current[pid][f.name] = null;
        }
        this.playerFormValues.set(current);
    }

    private seedPlayerValuesFromPriorRegistrations(schemas: PlayerProfileFieldSchema[]): void {
        try {
            const current = { ...this.playerFormValues() } as Record<string, Record<string, any>>;
            const players = this.familyPlayers();
            const schemaNameByLower: Record<string, string> = {};
            for (const s of schemas) schemaNameByLower[s.name.toLowerCase()] = s.name;
            for (const p of players) {
                if (!p.registered || !p.priorRegistrations?.length) continue;
                const pid = p.playerId;
                if (!current[pid]) continue;
                const source = p.priorRegistrations.at(-1);
                const fv = source?.formValues || {};
                for (const [rawK, rawV] of Object.entries(fv)) {
                    if (rawV == null || rawV === '') continue;
                    const kLower = rawK.toLowerCase();
                    let targetName = schemaNameByLower[kLower];
                    if (!targetName) {
                        const alias = this.formSchema.aliasFieldMap();
                        const foundAlias = Object.keys(alias).find(a => a.toLowerCase() === kLower);
                        if (foundAlias) targetName = alias[foundAlias];
                    }
                    if (!targetName) continue;
                    current[pid][targetName] = rawV;
                }
            }
            this.playerFormValues.set(current);
        } catch (e) {
            console.debug('[RegWizard] Prior registration seed failed', e);
        }
    }

    private applyAliasBackfill(): void {
        try {
            const alias = this.formSchema.aliasFieldMap();
            if (!alias || !Object.keys(alias).length) return;
            const current = { ...this.playerFormValues() } as Record<string, Record<string, any>>;
            for (const [, vals] of Object.entries(current)) {
                for (const [from, to] of Object.entries(alias)) {
                    if (from in vals && !(to in vals)) vals[to] = vals[from];
                }
            }
            this.playerFormValues.set(current);
        } catch (e) {
            console.debug('[RegWizard] Alias backfill failed', e);
        }
    }

    private seedEligibilityFromSchemas(schemas: PlayerProfileFieldSchema[]): void {
        try {
            const eligField = this.determineEligibilityField(schemas);
            if (!eligField) return;
            const map = { ...this.playerState.eligibilityByPlayer() } as Record<string, string>;
            for (const p of this.familyPlayers()) {
                if (!p.registered) continue;
                const v = this.playerFormValues()[p.playerId]?.[eligField];
                if (v != null && String(v).trim() !== '') map[p.playerId] = String(v);
            }
            if (!Object.keys(map).length) return;
            for (const [pid, val] of Object.entries(map)) this.playerState.setEligibilityForPlayer(pid, val);
            const selected = this.selectedPlayerIds();
            const values = selected.map(id => map[id]).filter(v => !!v);
            const unique = Array.from(new Set(values));
            if (unique.length === 1) this.teamConstraintValue.set(unique[0]);
        } catch { /* ignore */ }
    }

    private determineEligibilityField(schemas: PlayerProfileFieldSchema[]): string | null {
        const tctype = (this.teamConstraintType() || '').toUpperCase();
        if (!tctype || !schemas.length) return null;
        const candidates = schemas.filter(f => (f.visibility ?? 'public') !== 'hidden' && (f.visibility ?? 'public') !== 'adminOnly');
        const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
        const byName = (parts: string[]) => candidates.find(f => hasAll(f.name.toLowerCase(), parts) || hasAll(f.label.toLowerCase(), parts));
        if (tctype === 'BYGRADYEAR') return byName(['grad', 'year'])?.name || null;
        if (tctype === 'BYAGEGROUP') return byName(['age', 'group'])?.name || null;
        if (tctype === 'BYAGERANGE') return byName(['age', 'range'])?.name || null;
        if (tctype === 'BYCLUBNAME') return byName(['club'])?.name || null;
        return null;
    }

    /** Update a single player's field value */
    setPlayerFieldValue(playerId: string, fieldName: string, value: any): void {
        const all = { ...this.playerFormValues() };
        if (!all[playerId]) all[playerId] = {};
        all[playerId][fieldName] = value;
        this.playerFormValues.set(all);
        // Track US Lacrosse number value in usLaxStatus map when field updated
        if (fieldName.toLowerCase() === 'sportassnid') {
            const statusMap = { ...this.usLaxStatus() } as Record<string, { value: string; status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: any }>;
            const existing = statusMap[playerId] || { value: '', status: 'idle' };
            const raw = String(value ?? '').trim();
            // Dev/test bypass: immediately mark the well-known test number as valid without calling validator
            if (raw === '424242424242') {
                statusMap[playerId] = { ...existing, value: raw, status: 'valid', message: 'Test US Lax number accepted' };
            } else {
                statusMap[playerId] = { ...existing, value: raw, status: 'idle', message: undefined };
            }
            this.usLaxStatus.set(statusMap);
        }
    }

    /** Convenience accessor */
    getPlayerFieldValue(playerId: string, fieldName: string): any {
        return this.playerFormValues()[playerId]?.[fieldName];
    }

    // Removed signal sync shim; direct delegation methods reduce complexity

    /** Remove form values & team selections for players no longer selected (call after deselect) */
    pruneDeselectedPlayers(): void {
        const selectedIds = new Set(this.selectedPlayerIds());
        const forms = { ...this.playerFormValues() };
        const teams = { ...this.playerState.selectedTeams() };
        for (const pid of Object.keys(forms)) {
            if (!selectedIds.has(pid)) delete forms[pid];
        }
        for (const pid of Object.keys(teams)) {
            if (!selectedIds.has(pid)) delete teams[pid];
        }
        this.playerFormValues.set(forms);
        this.playerState.setSelectedTeams(teams);
    }

    // Removed unified context loader & snapshot apply; future: implement server-side context if needed.

    /**
     * Pre-submit API call: checks team roster capacity and creates pending registrations before payment.
     * Returns per-team results and next tab to show.
     */
    preSubmitRegistration(): Promise<PreSubmitRegistrationResponseDto> {
        const jobPath = this.jobPath();
        const familyUserId = this.familyUser()?.familyUserId;
        try { this.ensurePreSubmitPrerequisites(jobPath, familyUserId); } catch (e) {
            return Promise.reject(e);
        }
        // At this point non-null asserted by ensurePreSubmitPrerequisites
        const payload = this.buildPreSubmitPayload(jobPath!, familyUserId!);
        this.logPreSubmitPayloadIfLocal(payload);
        const base = this.resolveApiBase();
        return firstValueFrom(this.http.post<PreSubmitRegistrationResponseDto>(`${base}/registration/preSubmit`, payload))
            .then(resp => {
                this.captureServerValidationErrors(resp);
                this.processInsuranceOffer(resp);
                if (!resp) throw new Error('No response from preSubmit API');
                return resp;
            });
    }

    private ensurePreSubmitPrerequisites(jobPath: string | null, familyUserId: string | undefined): void {
        if (!jobPath || !familyUserId) throw new Error('Missing jobPath or familyUserId');
    }

    private buildPreSubmitPayload(jobPath: string, familyUserId: string): PreSubmitRegistrationRequestDto {
        const teamSelections: PreSubmitTeamSelectionDto[] = [];
        for (const pid of this.selectedPlayerIds()) {
            const teamId = this.playerState.selectedTeams()[pid];
            if (!teamId) continue;
            const formValues = this.buildPreSubmitFormValuesForPlayer(pid);
            this.injectRequiredTeamFieldIfNeeded(formValues, teamId);
            if (Array.isArray(teamId)) {
                for (const tid of teamId) teamSelections.push({ playerId: pid, teamId: tid, formValues });
            } else {
                teamSelections.push({ playerId: pid, teamId, formValues });
            }
        }
        return { jobPath, familyUserId, teamSelections };
    }

    private injectRequiredTeamFieldIfNeeded(formValues: { [key: string]: Json }, teamId: string | string[]): void {
        // Only inject if required teamId schema exists and value missing.
        const schema = this.profileFieldSchemas().find(s => s.name.toLowerCase() === 'teamid');
        if (!schema?.required) return;
        const existing = Object.entries(formValues).find(([k]) => k.toLowerCase() === 'teamid');
        if (existing && typeof existing[1] === 'string' && existing[1].trim()) return;
        if (typeof teamId === 'string') formValues['teamId'] = teamId;
        else if (Array.isArray(teamId) && teamId.length === 1 && typeof teamId[0] === 'string') formValues['teamId'] = teamId[0];
    }

    private logPreSubmitPayloadIfLocal(payload: PreSubmitRegistrationRequestDto): void {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost')) console.debug('[RegWizard] preSubmit payload', payload);
        } catch { /* ignore */ }
    }

    private captureServerValidationErrors(resp: PreSubmitRegistrationResponseDto): void {
        try {
            const ve = (resp as any)?.validationErrors as PreSubmitValidationErrorDto[] | undefined;
            this._serverValidationErrors = (ve && Array.isArray(ve) && ve.length) ? ve : [];
        } catch { this._serverValidationErrors = []; }
    }

    private processInsuranceOffer(resp: PreSubmitRegistrationResponseDto): void {
        try {
            const ins: any = (resp as any)?.insurance;
            if (ins?.available && ins?.playerObject) {
                this.verticalInsureOffer.set({ loading: false, data: ins.playerObject, error: null });
            } else if (ins?.error) {
                this.verticalInsureOffer.set({ loading: false, data: null, error: String(ins.error) });
            } else {
                this.verticalInsureOffer.set({ loading: false, data: null, error: null });
            }
        } catch { /* ignore */ }
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

        // Inject Eligibility selection as a normal form field so backend validation sees it
        // We only do this when the field is not already populated in visible values.
        try {
            const eligField = this.resolveEligibilityFieldNameFromSchemas();
            if (eligField) {
                const existing = out[eligField];
                const isMissing = existing == null || (typeof existing === 'string' && existing.trim() === '');
                const selected = this.getEligibilityForPlayer(playerId);
                if (isMissing && selected != null && String(selected).trim() !== '') {
                    out[eligField] = String(selected);
                }
            }
        } catch { /* best-effort */ }
        const waiverNames = Array.isArray(this.waiverFieldNames()) ? [...this.waiverFieldNames()] : [];
        if (waiverNames.length === 0) return out;
        // Deduplicate case-insensitively to avoid sending duplicates with different casing
        const seen = new Set<string>();
        const names = waiverNames.filter(n => {
            const k = String(n || '').toLowerCase();
            if (!k || seen.has(k)) {
                return false;
            }
            seen.add(k);
            return true;
        });
        const gateOk = !!this.waiversGateOk();
        try {
            for (const name of names) {
                if (gateOk) {
                    // The Waivers form is valid (all required accepted). Send true for all waiver fields.
                    out[name] = true;
                } else if (this.isWaiverAccepted(name)) {
                    // Best-effort injection: include only accepted as true; omit otherwise (server will validate).
                    out[name] = true;
                }
            }
        } catch (e) {
            console.warn('[RegWizard] Waiver acceptance injection failed – using map fallback', e);
            for (const name of names) if (this.isWaiverAccepted(name)) out[name] = true;
        }
        return out;
    }

    /** Determine the schema field name that represents the Eligibility selection (e.g., gradYear). */
    private resolveEligibilityFieldNameFromSchemas(): string | null {
        const tctype = (this.teamConstraintType() || '').toUpperCase();
        const schemas = this.profileFieldSchemas();
        if (!tctype || !schemas || schemas.length === 0) return null;
        const visible = schemas.filter(f => (f.visibility ?? 'public') !== 'hidden' && (f.visibility ?? 'public') !== 'adminOnly');
        const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
        const byNameOrLabel = (parts: string[]) => visible.find(f => hasAll(f.name.toLowerCase(), parts) || hasAll(f.label.toLowerCase(), parts));
        switch (tctype) {
            case 'BYGRADYEAR':
                return byNameOrLabel(['grad', 'year'])?.name || null;
            case 'BYAGEGROUP':
                return byNameOrLabel(['age', 'group'])?.name || null;
            case 'BYAGERANGE':
                return byNameOrLabel(['age', 'range'])?.name || null;
            case 'BYCLUBNAME':
                return byNameOrLabel(['club'])?.name || null;
            default:
                return null;
        }
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
        if (!this.isFieldVisibleForPlayer(playerId, field) || this.isPlayerLocked(playerId)) return null;
        const raw = this.getPlayerFieldValue(playerId, field.name);
        const str = raw == null ? '' : String(raw).trim();
        if (this.isRequiredInvalid(field, raw, str)) return 'Required';
        const typeError = this.validateBasicType(field, raw, str);
        if (typeError) return typeError;
        if (this.isUsLaxSchemaField(field)) return this.validateUsLaxField(playerId, field, str);
        return null;
    }

    private isRequiredInvalid(field: PlayerProfileFieldSchema, raw: any, str: string): boolean {
        if (!field.required) return false;
        if (field.type === 'checkbox') return raw !== true;
        if (field.type === 'multiselect') {
            return !Array.isArray(raw) || raw.length === 0;
        }
        return str.length === 0;
    }

    private validateBasicType(field: PlayerProfileFieldSchema, raw: any, str: string): string | null {
        if (str.length === 0 && field.type !== 'multiselect') return null; // empty non-required handled earlier
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
                if (field.options?.length && raw.some(v => !field.options.includes(String(v)))) return 'Invalid option';
                if (field.required && raw.length === 0) return 'Required';
                return null;
            default:
                return null;
        }
    }

    private validateUsLaxField(playerId: string, field: PlayerProfileFieldSchema, strVal: string): string | null {
        const statusEntry = this.usLaxStatus()[playerId];
        const status = statusEntry?.status || 'idle';
        if (strVal === '424242424242') return null; // test bypass
        const required = field.required;
        const isEmpty = strVal.length === 0;
        if (required && isEmpty) return 'Required';
        if (!required && isEmpty) return null;
        if (status === 'validating') return 'Validating…';
        if (status === 'invalid') return statusEntry?.message || 'Invalid membership';
        if (status !== 'valid') return 'Membership not validated';
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
// PreSubmit DTOs now imported from generated API models

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
