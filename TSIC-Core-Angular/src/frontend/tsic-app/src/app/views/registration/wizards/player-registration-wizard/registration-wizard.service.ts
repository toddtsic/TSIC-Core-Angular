import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, Observable, Subscription } from 'rxjs';
import { tap } from 'rxjs/operators';
import { PlayerStateService } from './services/player-state.service';
import { WaiverStateService } from './services/waiver-state.service';
import { FormSchemaService } from './services/form-schema.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { Loadable } from '@infrastructure/shared/state.models';
import type {
    VIPlayerObjectResponse,
    PreSubmitPlayerRegistrationRequestDto,
    PreSubmitPlayerRegistrationResponseDto,
    PreSubmitTeamSelectionDto,
    PreSubmitValidationErrorDto,
    FamilyPlayersResponseDto,
    FamilyPlayerDto,
    FamilyPlayerRegistrationDto,
    RegSaverDetailsDto,
    PlayerRegConfirmationDto,
    AuthTokenResponse
} from '@core/api';
import { environment } from '@environments/environment';
import { getPropertyCI, pickStringCI, hasAllParts } from '../shared/utils/property-utils';

export type PaymentOption = 'PIF' | 'Deposit' | 'ARB';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    private readonly playerState = inject(PlayerStateService);
    private readonly auth = inject(AuthService);
    // Delegated services (extracted logic)
    private readonly waiverState = inject(WaiverStateService);
    private readonly formSchema = inject(FormSchemaService);
    // Job context
    private readonly _jobPath = signal<string>('');
    private readonly _jobId = signal<string>('');
    readonly jobPath = this._jobPath.asReadonly();
    readonly jobId = this._jobId.asReadonly();

    // Family account presence (from Family Check step)
    private readonly _hasFamilyAccount = signal<'yes' | 'no' | null>(null);
    readonly hasFamilyAccount = this._hasFamilyAccount.asReadonly();

    // Players and selections
    // Family players enriched with prior registrations + selection flag
    private readonly _familyPlayers = signal<FamilyPlayerDto[]>([]);
    private readonly _familyPlayersLoading = signal<boolean>(false);
    readonly familyPlayers = this._familyPlayers.asReadonly();
    readonly familyPlayersLoading = this._familyPlayersLoading.asReadonly();
    // Family user summary (from players endpoint). Includes optional contact fields for convenience defaults on Payment.
    private readonly _familyUser = signal<{
        familyUserId: string;
        displayName: string;
        userName: string;
        firstName?: string;
        lastName?: string;
        address?: string;
        address1?: string;
        address2?: string;
        city?: string;
        state?: string;
        zipCode?: string;
        zip?: string;
        postalCode?: string;
        email?: string;
        phone?: string;
        ccInfo?: {
            firstName?: string;
            lastName?: string;
            streetAddress?: string;
            zip?: string;
            email?: string;
            phone?: string;
        };
    } | null>(null);
    readonly familyUser = this._familyUser.asReadonly();
    // RegSaver (optional insurance) details for family/job
    private readonly _regSaverDetails = signal<RegSaverDetailsDto | null>(null);
    readonly regSaverDetails = this._regSaverDetails.asReadonly();
    // Design Principle: EACH REGISTRATION OWNS ITS OWN SNAPSHOT OF FORM VALUES.
    // We do NOT merge or unify formValues across multiple registrations for a player.
    // Any edit that creates a new registration stamps values only into that new registration.
    // Prior registrations remain immutable snapshots (unless explicitly edited one-by-one in future flows).
    // Whether an existing player registration for the current job + active family user already exists.
    // null = unknown/not yet checked; true/false = definitive.
    // Eligibility type is job-wide (e.g., BYGRADYEAR), but the selected value is PER PLAYER
    private readonly _teamConstraintType = signal<string | null>(null);
    private readonly _teamConstraintValue = signal<string | null>(null);
    readonly teamConstraintType = this._teamConstraintType.asReadonly();
    readonly teamConstraintValue = this._teamConstraintValue.asReadonly();
    // Backward-compatible facade methods for selectedTeams
    selectedTeams(): Record<string, string | string[]> { return this.playerState.selectedTeams(); }
    setSelectedTeams(map: Record<string, string | string[]>): void { this.playerState.setSelectedTeams(map); }
    // Eligibility facades
    eligibilityByPlayer(): Record<string, string> { return this.playerState.eligibilityByPlayer(); }
    setEligibilityForPlayer(playerId: string, value: string | null | undefined): void { this.playerState.setEligibilityForPlayer(playerId, value); }
    getEligibilityForPlayer(playerId: string): string | undefined { return this.playerState.getEligibilityForPlayer(playerId); }
    // (migrated) eligibility & selectedTeams now owned by PlayerStateService
    // Job metadata raw JSON snapshots
    private readonly _jobProfileMetadataJson = signal<string | null>(null);
    private readonly _jobJsonOptions = signal<string | null>(null);
    readonly jobProfileMetadataJson = this._jobProfileMetadataJson.asReadonly();
    readonly jobJsonOptions = this._jobJsonOptions.asReadonly();
    // Job payment feature flags
    private readonly _jobHasActiveDiscountCodes = signal<boolean>(false);
    private readonly _jobUsesAmex = signal<boolean>(false);
    readonly jobHasActiveDiscountCodes = this._jobHasActiveDiscountCodes.asReadonly();
    readonly jobUsesAmex = this._jobUsesAmex.asReadonly();
    // Payment flags & ARB schedule
    private readonly _adnArb = signal<boolean>(false);
    private readonly _adnArbBillingOccurences = signal<number | null>(null);
    private readonly _adnArbIntervalLength = signal<number | null>(null);
    private readonly _adnArbStartDate = signal<string | null>(null);
    readonly adnArb = this._adnArb.asReadonly();
    readonly adnArbBillingOccurences = this._adnArbBillingOccurences.asReadonly();
    readonly adnArbIntervalLength = this._adnArbIntervalLength.asReadonly();
    readonly adnArbStartDate = this._adnArbStartDate.asReadonly();
    // Parsed field schema derived from PlayerProfileMetadataJson
    private readonly _profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
    private readonly _aliasFieldMap = signal<Record<string, string>>({});
    readonly profileFieldSchemas = this._profileFieldSchemas.asReadonly();
    readonly aliasFieldMap = this._aliasFieldMap.asReadonly();
    // Per-player form values (fieldName -> value)
    private readonly _playerFormValues = signal<Record<string, Record<string, string | number | boolean | null | string[]>>>({});
    readonly playerFormValues = this._playerFormValues.asReadonly();
    // Waiver text HTML blocks (job-level)
    private readonly _jobWaivers = signal<Record<string, string>>({});
    readonly jobWaivers = this._jobWaivers.asReadonly();
    // Waiver delegating accessors (maintain existing call sites that invoke like signals)
    waiverDefinitions(): WaiverDefinition[] { return this.waiverState.waiverDefinitions(); }
    waiverIdToField(): Record<string, string> { return this.waiverState.waiverIdToField(); }
    waiversAccepted(): Record<string, boolean> { return this.waiverState.waiversAccepted(); }
    waiverFieldNames(): string[] { return this.waiverState.waiverFieldNames(); }
    waiversGateOk(): boolean { return this.waiverState.waiversGateOk(); }
    setWaiversGateOk(v: boolean): void { this.waiverState.setWaiversGateOk(v); }
    signatureName(): string { return this.waiverState.signatureName(); }
    setSignatureName(v: string): void { this.waiverState.setSignatureName(v); }
    signatureRole(): 'Parent/Guardian' | 'Adult Player' | '' { return this.waiverState.signatureRole(); }
    setSignatureRole(v: 'Parent/Guardian' | 'Adult Player' | ''): void { this.waiverState.setSignatureRole(v); }
    // Dev-only: capture a normalized snapshot of GET /family/players for the debug panel on Players step
    private readonly _debugFamilyPlayersResp = signal<FamilyPlayersResponseDto | null>(null);
    readonly debugFamilyPlayersResp = this._debugFamilyPlayersResp.asReadonly();

    // US Lacrosse number validation status per player
    private readonly _usLaxStatus = signal<Record<string, { value: string; status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: Record<string, unknown> }>>({});
    readonly usLaxStatus = this._usLaxStatus.asReadonly();
    // Subscription management for fire-and-forget HTTP calls
    private familyPlayersSub?: Subscription;
    private metadataSub?: Subscription;

    // Server-side validation errors captured from latest preSubmit response (playerId/field/message).
    // Empty array when none; undefined before first preSubmit call.
    private _serverValidationErrors: PreSubmitValidationErrorDto[] | undefined;
    getServerValidationErrors(): PreSubmitValidationErrorDto[] { return this._serverValidationErrors ? [...this._serverValidationErrors] : []; }
    hasServerValidationErrors(): boolean { return !!this._serverValidationErrors?.length; }

    // Payment
    private readonly _paymentOption = signal<PaymentOption>('PIF');
    readonly paymentOption = this._paymentOption.asReadonly();
    // Last payment summary for Confirmation step
    private readonly _lastPayment = signal<{
        option: PaymentOption;
        amount: number;
        transactionId?: string;
        subscriptionId?: string;
        viPolicyNumber?: string | null;
        viPolicyCreateDate?: string | null;
        message?: string | null;
    } | null>(null);
    readonly lastPayment = this._lastPayment.asReadonly();

    // Confirmation DTO from backend (Finish tab)
    private readonly _confirmation = signal<PlayerRegConfirmationDto | null>(null);
    readonly confirmation = this._confirmation.asReadonly();

    reset(): void {
        this._hasFamilyAccount.set(null);
        this._familyPlayers.set([]);
        this.playerState.reset();
        this._teamConstraintType.set(null);
        this._teamConstraintValue.set(null);
        this._paymentOption.set('PIF');
        this._lastPayment.set(null);
        this._confirmation.set(null);
        this._familyUser.set(null);
        this._jobProfileMetadataJson.set(null);
        this._jobJsonOptions.set(null);
        this._profileFieldSchemas.set([]);
        this._playerFormValues.set({});
        this._jobWaivers.set({});
        // Waiver state reset handled by WaiverStateService automatically (no local reset needed)
        this._adnArb.set(false);
        this._adnArbBillingOccurences.set(null);
        this._adnArbIntervalLength.set(null);
        this._adnArbStartDate.set(null);
        this._jobHasActiveDiscountCodes.set(false);
        this._jobUsesAmex.set(false);
    }

    /**
     * Clear family-specific state when the authenticated user changes, while preserving job context.
     */
    resetForFamilySwitch(): void {
        const jp = this.jobPath();
        const jid = this.jobId();
        this.reset();
        if (jp) this._jobPath.set(jp);
        if (jid) this._jobId.set(jid);
    }

    // --- Controlled mutators for signals written by step components ---
    setJobPath(v: string): void { this._jobPath.set(v); }
    setJobId(v: string): void { this._jobId.set(v); }
    setHasFamilyAccount(v: 'yes' | 'no' | null): void { this._hasFamilyAccount.set(v); }
    setTeamConstraintType(v: string | null): void { this._teamConstraintType.set(v); }
    setTeamConstraintValue(v: string | null): void { this._teamConstraintValue.set(v); }
    setPaymentOption(v: PaymentOption): void { this._paymentOption.set(v); }
    setLastPayment(v: { option: PaymentOption; amount: number; transactionId?: string; subscriptionId?: string; viPolicyNumber?: string | null; viPolicyCreateDate?: string | null; message?: string | null } | null): void { this._lastPayment.set(v); }
    setConfirmation(v: PlayerRegConfirmationDto | null): void { this._confirmation.set(v); }
    updateFamilyPlayers(players: FamilyPlayerDto[]): void { this._familyPlayers.set(players); }
    clearDebugFamilyPlayersResp(): void { this._debugFamilyPlayersResp.set(null); }

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

    /** Loader: returns family user summary + family players (registered + server-selected flag) + optional RegSaver details.
     * Minimal auth gating: skip call if no stored access token (prevents expected 401 on hard refresh deep-link).
     */
    loadFamilyPlayers(jobPath: string): void {
        if (!this.shouldLoadFamily(jobPath)) return;
        const base = this.resolveApiBase();
        console.debug('[RegWizard] GET family players', { jobPath, base });
        this._familyPlayersLoading.set(true);
        this.familyPlayersSub?.unsubscribe();
        this.familyPlayersSub = this.http.get<FamilyPlayersResponseDto>(`${base}/family/players`, { params: { jobPath, debug: '1' } })
            .subscribe({
                next: resp => {
                    this.handleFamilyPlayersSuccess(resp, jobPath);
                    this._familyPlayersLoading.set(false);
                },
                error: (err: unknown) => {
                    this.handleFamilyPlayersError(err);
                    this._familyPlayersLoading.set(false);
                }
            });
    }

    /**
     * Upgrades Phase 1 token to job-scoped token (adds jobPath claim, NO regId).
     * Called after family login in player wizard.
     */
    setWizardContext(jobPath: string): Observable<AuthTokenResponse> {
        const base = this.resolveApiBase();
        return this.http.post<AuthTokenResponse>(
            `${base}/player-registration/set-wizard-context`,
            { jobPath }
        ).pipe(
            tap(response => {
                if (response.accessToken) {
                    this.auth.applyNewToken(response.accessToken);
                }
            })
        );
    }

    async loadFamilyPlayersOnce(jobPath: string): Promise<void> {
        if (!this.shouldLoadFamily(jobPath)) return;
        const base = this.resolveApiBase();
        this._familyPlayersLoading.set(true);
        try {
            const resp = await firstValueFrom(this.http.get<FamilyPlayersResponseDto>(`${base}/family/players`, { params: { jobPath, debug: '1' } }));
            this.handleFamilyPlayersSuccess(resp, jobPath);
        } catch (err: unknown) {
            this.handleFamilyPlayersError(err);
            throw err;
        } finally {
            this._familyPlayersLoading.set(false);
        }
    }

    private shouldLoadFamily(jobPath: string | null | undefined): boolean {
        if (!jobPath) return false;
        if (!this.auth.getToken()) {
            console.debug('[RegWizard] loadFamilyPlayers skipped (no auth token)');
            return false;
        }
        return true;
    }

    private handleFamilyPlayersSuccess(resp: FamilyPlayersResponseDto, jobPath: string): void {
        this._debugFamilyPlayersResp.set(resp);
        this.extractConstraintType(resp);
        this.applyFamilyUser(resp);
        this.applyRegSaverDetails(resp);
        const players = this.buildFamilyPlayersList(resp);
        this._familyPlayers.set(players);
        this.prefillTeamsFromPriorRegistrations(players);
        this.ensureJobMetadata(jobPath);
        this.applyPaymentFlags(resp);
    }

    private handleFamilyPlayersError(err: unknown): void {
        console.warn('[RegWizard] Failed to load family players', err);
        this._familyPlayers.set([]);
        this._familyUser.set(null);
        this._regSaverDetails.set(null);
        this._debugFamilyPlayersResp.set(null);
    }

    private extractConstraintType(resp: FamilyPlayersResponseDto): void {
        try {
            const jrf = resp.jobRegForm || getPropertyCI<FamilyPlayersResponseDto['jobRegForm']>(resp as Record<string, unknown>, 'jobRegForm');
            const rawCt = jrf?.constraintType ?? null;
            if (typeof rawCt === 'string' && rawCt.trim()) {
                const norm = rawCt.trim().toUpperCase();
                this._teamConstraintType.set(norm);
                console.debug('[RegWizard] constraintType from /family/players:', norm);
            } else {
                console.warn('[RegWizard] constraintType not present in /family/players response; Eligibility step may be hidden.');
            }
        } catch { /* ignore */ }
    }

    private applyFamilyUser(resp: FamilyPlayersResponseDto): void {
        const fu = resp.familyUser || getPropertyCI<Record<string, unknown>>(resp as Record<string, unknown>, 'familyUser');
        if (!fu) { this._familyUser.set(null); return; }
        const o = fu as Record<string, unknown>;
        const norm = {
            familyUserId: o['familyUserId'] as string,
            displayName: o['displayName'] as string,
            userName: o['userName'] as string,
            firstName: pickStringCI(o, 'firstName', 'parentFirstName', 'motherFirstName', 'guardianFirstName', 'billingFirstName'),
            lastName: pickStringCI(o, 'lastName', 'parentLastName', 'motherLastName', 'guardianLastName', 'billingLastName'),
            address: pickStringCI(o, 'address', 'billingAddress', 'street', 'street1', 'address1'),
            address1: pickStringCI(o, 'address1', 'street1'),
            address2: pickStringCI(o, 'address2', 'street2', 'apt', 'aptNumber', 'suite'),
            city: pickStringCI(o, 'city'),
            state: pickStringCI(o, 'state', 'stateCode'),
            zipCode: pickStringCI(o, 'zipCode', 'zip', 'postalCode'),
            zip: pickStringCI(o, 'zip'),
            postalCode: pickStringCI(o, 'postalCode'),
            email: pickStringCI(o, 'email', 'parentEmail', 'motherEmail', 'guardianEmail', 'billingEmail', 'userName'),
            phone: (() => {
                const raw = pickStringCI(o, 'phone', 'parentPhone', 'motherPhone', 'guardianPhone', 'billingPhone', 'phoneNumber', 'cellPhone', 'mobile');
                return raw ? raw.replaceAll(/\D+/g, '') : undefined;
            })(),
            ccInfo: undefined as { firstName?: string; lastName?: string; streetAddress?: string; zip?: string; email?: string; phone?: string } | undefined
        };
        const rawCc = resp.ccInfo || getPropertyCI<Record<string, unknown>>(resp as Record<string, unknown>, 'ccInfo');
        if (rawCc) {
            const cc = rawCc as Record<string, unknown>;
            norm.ccInfo = {
                firstName: pickStringCI(cc, 'firstName'),
                lastName: pickStringCI(cc, 'lastName'),
                streetAddress: pickStringCI(cc, 'streetAddress', 'address'),
                zip: pickStringCI(cc, 'zip', 'zipCode', 'postalCode'),
                email: pickStringCI(cc, 'email'),
                phone: (() => {
                    const raw = pickStringCI(cc, 'phone');
                    return raw ? raw.replaceAll(/\D+/g, '') : undefined;
                })()
            };
        }
        this._familyUser.set(norm);
    }

    private applyRegSaverDetails(resp: FamilyPlayersResponseDto): void {
        const rs = resp.regSaverDetails || getPropertyCI<RegSaverDetailsDto>(resp as Record<string, unknown>, 'regSaverDetails');
        if (!rs) { this._regSaverDetails.set(null); return; }
        this._regSaverDetails.set({
            policyNumber: rs.policyNumber,
            policyCreateDate: rs.policyCreateDate
        });
    }

    private buildFamilyPlayersList(resp: FamilyPlayersResponseDto): FamilyPlayerDto[] {
        const r = resp as Record<string, unknown>;
        const rawPlayers: Record<string, unknown>[] = (resp.familyPlayers as Record<string, unknown>[]) || getPropertyCI<Record<string, unknown>[]>(r, 'familyPlayers', 'players') || [];
        return rawPlayers.map(p => {
            const prior: Record<string, unknown>[] = getPropertyCI<Record<string, unknown>[]>(p, 'priorRegistrations') || [];
            const priorRegs: FamilyPlayerRegistrationDto[] = prior.map(r => {
                const fin = getPropertyCI<Record<string, unknown>>(r, 'financials');
                return {
                    registrationId: getPropertyCI<string>(r, 'registrationId') ?? '',
                    active: !!getPropertyCI<boolean>(r, 'active'),
                    financials: {
                        feeBase: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeBase') ?? 0),
                        feeProcessing: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeProcessing') ?? 0),
                        feeDiscount: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeDiscount') ?? 0),
                        feeDonation: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeDonation') ?? 0),
                        feeLateFee: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeLateFee') ?? 0),
                        feeTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeTotal') ?? 0),
                        owedTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'owedTotal') ?? 0),
                        paidTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'paidTotal') ?? 0)
                    },
                    assignedTeamId: getPropertyCI<string>(r, 'assignedTeamId') ?? undefined,
                    assignedTeamName: getPropertyCI<string>(r, 'assignedTeamName') ?? undefined,
                    adnSubscriptionId: (r['adnSubscriptionId'] as string) ?? undefined,
                    adnSubscriptionStatus: (r['adnSubscriptionStatus'] as string) ?? undefined,
                    adnSubscriptionAmountPerOccurence: (r['adnSubscriptionAmountPerOccurence'] as number) ?? undefined,
                    adnSubscriptionBillingOccurences: (r['adnSubscriptionBillingOccurences'] as number) ?? undefined,
                    adnSubscriptionIntervalLength: (r['adnSubscriptionIntervalLength'] as number) ?? undefined,
                    adnSubscriptionStartDate: (r['adnSubscriptionStartDate'] as string) ?? undefined,
                    formFieldValues: (getPropertyCI<Record<string, unknown>>(r, 'formFieldValues', 'formValues') ?? {}) as Record<string, unknown>
                };
            });
            return {
                playerId: getPropertyCI<string>(p, 'playerId') ?? '',
                firstName: getPropertyCI<string>(p, 'firstName') ?? '',
                lastName: getPropertyCI<string>(p, 'lastName') ?? '',
                gender: getPropertyCI<string>(p, 'gender') ?? '',
                dob: getPropertyCI<string>(p, 'dob') ?? undefined,
                registered: !!getPropertyCI<boolean>(p, 'registered'),
                selected: !!getPropertyCI<boolean>(p, 'selected') || !!getPropertyCI<boolean>(p, 'registered'),
                priorRegistrations: priorRegs
            } as FamilyPlayerDto;
        });
    }

    private prefillTeamsFromPriorRegistrations(players: FamilyPlayerDto[]): void {
        const teamMap: Record<string, string | string[]> = { ...this.playerState.selectedTeams() };
        for (const fp of players) {
            if (!fp.registered) continue;
            const teamIds = fp.priorRegistrations
                .map(r => r.assignedTeamId)
                .filter((id): id is string => typeof id === 'string' && !!id);
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
            this._jobHasActiveDiscountCodes.set(!!resp.jobHasActiveDiscountCodes);
            this._jobUsesAmex.set(!!resp.jobUsesAmex);
        } catch { /* ignore */ }
    }

    /** Fetch job metadata if not already loaded so we can parse PlayerProfileMetadataJson & JsonOptions. */
    ensureJobMetadata(jobPath: string): void {
        if (!jobPath) return;
        if (this.jobProfileMetadataJson() && this.jobJsonOptions()) return; // already have it
        const base = this.resolveApiBase();
        this.metadataSub?.unsubscribe();
        this.metadataSub = this.http.get<{ jobId: string; playerProfileMetadataJson?: string | null; jsonOptions?: string | null;[k: string]: unknown }>(`${base}/jobs/${encodeURIComponent(jobPath)}`)
            .subscribe({
                next: meta => {
                    this._jobId.set(meta.jobId);
                    this._jobProfileMetadataJson.set(meta.playerProfileMetadataJson || null);
                    this._jobJsonOptions.set(meta.jsonOptions || null);
                    // Payment flags & schedule from server metadata
                    try {
                        const m = meta as Record<string, unknown>;
                        const arb = getPropertyCI<boolean>(m, 'adnArb') ?? false;
                        const occ = getPropertyCI<number>(m, 'adnArbBillingOccurences') ?? null;
                        const intLen = getPropertyCI<number>(m, 'adnArbIntervalLength') ?? null;
                        const start = getPropertyCI<string>(m, 'adnArbStartDate') ?? null;
                        this._adnArb.set(!!arb);
                        this._adnArbBillingOccurences.set(typeof occ === 'number' ? occ : null);
                        this._adnArbIntervalLength.set(typeof intLen === 'number' ? intLen : null);
                        this._adnArbStartDate.set(start ? String(start) : null);
                        // Default to ARB when enabled; else PIF. UI will adjust based on scenario when teams are selected.
                        this._paymentOption.set(this.adnArb() ? 'ARB' : 'PIF');
                    } catch { /* non-critical */ }
                    // Do not set constraintType from client-side heuristics; rely solely on /family/players response.
                    // Offer flag for RegSaver
                    try {
                        const offer = getPropertyCI<boolean>(meta as Record<string, unknown>, 'offerPlayerRegsaverInsurance');
                        this._offerPlayerRegSaver.set(!!offer);
                    } catch { this._offerPlayerRegSaver.set(false); }
                    // Do not infer Eligibility constraint type on the client. Rely solely on server-provided jobRegForm.constraintType from /family/players.
                    // Delegate waiver extraction + definition building to WaiverStateService
                    const waivers = this.waiverState.buildFromMetadata(
                        meta,
                        this.jobProfileMetadataJson(),
                        this.selectedPlayerIds(),
                        this.familyPlayers()
                    );
                    this._jobWaivers.set(waivers);
                    this.parseProfileMetadata();
                },
                error: (err: unknown) => {
                    console.error('[RegWizard] Failed to load job metadata for form parsing', err);
                }
            });
    }
    // RegSaver offer flag (job-level)
    private readonly _offerPlayerRegSaver = signal(false);
    // VerticalInsure offer state retained (widget/playerObject payload) for preSubmit response integration
    private readonly _verticalInsureOffer = signal<Loadable<VIPlayerObjectResponse>>({ loading: false, data: null, error: null });
    readonly verticalInsureOffer = this._verticalInsureOffer.asReadonly();
    /** Whether the job offers player RegSaver insurance */
    readonly offerPlayerRegSaver = this._offerPlayerRegSaver.asReadonly();

    // Removed direct fetch of VerticalInsure player-object; preSubmit response is now the single source of truth.
    /** Delegated schema + waiver processing via extracted services. */
    private parseProfileMetadata(): void {
        const rawMeta = this.jobProfileMetadataJson();
        const rawOpts = this.jobJsonOptions();
        this.formSchema.parse(rawMeta, rawOpts);
        const schemas = this.formSchema.profileFieldSchemas();
        this._profileFieldSchemas.set(schemas);
        this._aliasFieldMap.set(this.formSchema.aliasFieldMap());
        this.bindWaiversToSchemas(schemas);
        this.initializeFormValuesForSelectedPlayers(schemas);
        this.seedPlayerValuesFromPriorRegistrations(schemas);
        this.seedPlayerValuesFromDefaults(schemas); // merge defaults for unregistered players
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
        const current = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        for (const pid of selectedIds) {
            if (!current[pid]) current[pid] = {};
            for (const f of schemas) if (!(f.name in current[pid])) current[pid][f.name] = null;
        }
        this._playerFormValues.set(current);
    }

    private seedPlayerValuesFromPriorRegistrations(schemas: PlayerProfileFieldSchema[]): void {
        try {
            const current = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            const schemaNameByLower = this.buildSchemaLookup(schemas);

            for (const player of this.familyPlayers()) {
                if (!player.registered || !player.priorRegistrations?.length) continue;
                if (!current[player.playerId]) continue;

                const latestRegistration = player.priorRegistrations.at(-1);
                const formFieldValues = latestRegistration?.formFieldValues || {};

                this.copyFormFieldValues(
                    formFieldValues,
                    current[player.playerId],
                    schemaNameByLower
                );
            }

            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[RegWizard] Prior registration seed failed', e);
        }
    }

    private copyFormFieldValues(
        source: Record<string, unknown>,
        target: Record<string, unknown>,
        schemaNameByLower: Record<string, string>
    ): void {
        for (const [rawKey, rawValue] of Object.entries(source)) {
            if (rawValue == null || rawValue === '') continue;

            const targetName = this.resolveFieldName(rawKey, schemaNameByLower);
            if (targetName) {
                target[targetName] = rawValue;
            }
        }
    }

    private seedPlayerValuesFromDefaults(schemas: PlayerProfileFieldSchema[]): void {
        try {
            const current = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            const schemaNameByLower = this.buildSchemaLookup(schemas);

            for (const player of this.familyPlayers()) {
                if (this.shouldSkipPlayerForDefaults(player)) continue;
                this.applyDefaultsToPlayer(player, current, schemaNameByLower);
            }

            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[RegWizard] Default values seed failed', e);
        }
    }

    private buildSchemaLookup(schemas: PlayerProfileFieldSchema[]): Record<string, string> {
        const lookup: Record<string, string> = {};
        for (const s of schemas) {
            lookup[s.name.toLowerCase()] = s.name;
        }
        return lookup;
    }

    private shouldSkipPlayerForDefaults(player: FamilyPlayerDto): boolean {
        return player.registered || !player.selected;
    }

    private applyDefaultsToPlayer(
        player: FamilyPlayerDto,
        formValues: Record<string, Record<string, unknown>>,
        schemaNameByLower: Record<string, string>
    ): void {
        const pid = player.playerId;
        if (!formValues[pid]) return;

        const defaults = player.defaultFieldValues || {};
        for (const [rawKey, rawValue] of Object.entries(defaults)) {
            if (rawValue == null || rawValue === '') continue;

            const targetName = this.resolveFieldName(rawKey, schemaNameByLower);
            if (!targetName) continue;

            const existing = formValues[pid][targetName];
            if (this.isFieldValueBlank(existing)) {
                formValues[pid][targetName] = rawValue;
            }
        }
    }

    private resolveFieldName(rawKey: string, schemaNameByLower: Record<string, string>, alias?: Record<string, string>): string | null {
        const kLower = rawKey.toLowerCase();
        let targetName = schemaNameByLower[kLower];

        if (!targetName) {
            const aliasMap = alias ?? this.formSchema.aliasFieldMap();
            const foundAlias = Object.keys(aliasMap).find(a => a.toLowerCase() === kLower);
            if (foundAlias) targetName = aliasMap[foundAlias];
        }

        return targetName || null;
    }

    private applyAliasBackfill(): void {
        try {
            const alias = this.formSchema.aliasFieldMap();
            if (!alias || !Object.keys(alias).length) return;
            const current = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
            for (const [, vals] of Object.entries(current)) {
                for (const [from, to] of Object.entries(alias)) {
                    if (from in vals && !(to in vals)) vals[to] = vals[from];
                }
            }
            this._playerFormValues.set(current);
        } catch (e: unknown) {
            console.debug('[RegWizard] Alias backfill failed', e);
        }
    }

    private seedEligibilityFromSchemas(schemas: PlayerProfileFieldSchema[]): void {
        try {
            const eligField = this.determineEligibilityField(schemas);
            if (!eligField) return;
            const map = { ...this.playerState.eligibilityByPlayer() } as Record<string, string>;
            const players = this.familyPlayers();
            for (const p of players) {
                // Include: registered players (locked) OR currently selected unregistered players
                if (!p.registered && !p.selected) continue;
                const v = this.playerFormValues()[p.playerId]?.[eligField];
                if (v != null && String(v).trim() !== '') {
                    // Do not overwrite an existing eligibility selection unless blank
                    const existing = map[p.playerId];
                    if (!existing || String(existing).trim() === '') map[p.playerId] = String(v).trim();
                }
            }
            if (!Object.keys(map).length) return;
            for (const [pid, val] of Object.entries(map)) this.playerState.setEligibilityForPlayer(pid, val);
            const selected = this.selectedPlayerIds();
            const values = selected.map(id => map[id]).filter(v => !!v);
            const unique = Array.from(new Set(values));
            if (unique.length === 1) this._teamConstraintValue.set(unique[0]);
        } catch { /* ignore */ }
    }

    private determineEligibilityField(schemas: PlayerProfileFieldSchema[]): string | null {
        const tctype = (this.teamConstraintType() || '').toUpperCase();
        if (!tctype || !schemas.length) return null;
        const candidates = schemas.filter(f => (f.visibility ?? 'public') !== 'hidden' && (f.visibility ?? 'public') !== 'adminOnly');
        const byName = (parts: string[]) => candidates.find(f => hasAllParts(f.name.toLowerCase(), parts) || hasAllParts(f.label.toLowerCase(), parts));
        if (tctype === 'BYGRADYEAR') return byName(['grad', 'year'])?.name || null;
        if (tctype === 'BYAGEGROUP') return byName(['age', 'group'])?.name || null;
        if (tctype === 'BYAGERANGE') return byName(['age', 'range'])?.name || null;
        if (tctype === 'BYCLUBNAME') return byName(['club'])?.name || null;
        return null;
    }

    /** Update a single player's field value */
    setPlayerFieldValue(playerId: string, fieldName: string, value: string | number | boolean | null | string[]): void {
        const all = { ...this.playerFormValues() };
        if (!all[playerId]) all[playerId] = {};
        all[playerId][fieldName] = value;
        this._playerFormValues.set(all);
        // Track US Lacrosse number value in usLaxStatus map when field updated
        if (fieldName.toLowerCase() === 'sportassnid') {
            const statusMap = { ...this.usLaxStatus() } as Record<string, { value: string; status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: Record<string, unknown> }>;
            const existing = statusMap[playerId] || { value: '', status: 'idle' };
            const raw = String(value ?? '').trim();
            // Dev/test bypass: immediately mark the well-known test number as valid without calling validator
            if (raw === '424242424242') {
                statusMap[playerId] = { ...existing, value: raw, status: 'valid', message: 'Test US Lax number accepted' };
            } else {
                statusMap[playerId] = { ...existing, value: raw, status: 'idle', message: undefined };
            }
            this._usLaxStatus.set(statusMap);
        }
    }

    /** Convenience accessor */
    getPlayerFieldValue(playerId: string, fieldName: string): unknown {
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
        this._playerFormValues.set(forms);
        this.playerState.setSelectedTeams(teams);
    }

    // Removed unified context loader & snapshot apply; future: implement server-side context if needed.

    /**
     * Pre-submit API call: checks team roster capacity and creates pending registrations before payment.
     * Returns per-team results and next tab to show.
     */
    preSubmitRegistration(): Promise<PreSubmitPlayerRegistrationResponseDto> {
        const jobPath = this.jobPath();
        const familyUserId = this.familyUser()?.familyUserId;
        try { this.ensurePreSubmitPrerequisites(jobPath, familyUserId); } catch (e: unknown) {
            return Promise.reject(e);
        }
        // At this point non-null validated by ensurePreSubmitPrerequisites
        const payload = this.buildPreSubmitPayload(jobPath, familyUserId);
        this.logPreSubmitPayloadIfLocal(payload);
        const base = this.resolveApiBase();
        return firstValueFrom(this.http.post<PreSubmitPlayerRegistrationResponseDto>(`${base}/player-registration/preSubmit`, payload))
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

    private buildPreSubmitPayload(jobPath: string | null | undefined, familyUserId: string | null | undefined): PreSubmitPlayerRegistrationRequestDto {
        if (!jobPath || !familyUserId) {
            throw new Error('jobPath and familyUserId are required');
        }
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
        return { jobPath, teamSelections };
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

    private logPreSubmitPayloadIfLocal(payload: PreSubmitPlayerRegistrationRequestDto): void {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost')) console.debug('[RegWizard] preSubmit payload', payload);
        } catch { /* ignore */ }
    }

    private captureServerValidationErrors(resp: PreSubmitPlayerRegistrationResponseDto): void {
        try {
            const ve = getPropertyCI<PreSubmitValidationErrorDto[]>(resp as Record<string, unknown>, 'validationErrors');
            this._serverValidationErrors = (ve && Array.isArray(ve) && ve.length) ? ve : [];
        } catch { this._serverValidationErrors = []; }
    }

    private processInsuranceOffer(resp: PreSubmitPlayerRegistrationResponseDto): void {
        try {
            const ins = getPropertyCI<{ available?: boolean; playerObject?: unknown; error?: unknown }>(resp as Record<string, unknown>, 'insurance');
            if (ins?.available && ins?.playerObject) {
                this._verticalInsureOffer.set({ loading: false, data: ins.playerObject, error: null });
            } else if (ins?.error) {
                this._verticalInsureOffer.set({ loading: false, data: null, error: String(ins.error) });
            } else {
                this._verticalInsureOffer.set({ loading: false, data: null, error: null });
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
        const all = this.playerFormValues()[playerId] || {} as Record<string, unknown>;
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
                // If all waivers are accepted OR this specific waiver is accepted, include it
                if (gateOk || this.isWaiverAccepted(name)) {
                    out[name] = true;
                }
            }
        } catch (e: unknown) {
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
        const byNameOrLabel = (parts: string[]) => visible.find(f => hasAllParts(f.name.toLowerCase(), parts) || hasAllParts(f.label.toLowerCase(), parts));
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

    /** Load confirmation summary (financial + insurance + substituted HTML) after payment/insurance flows complete. */
    loadConfirmation(): void {
        const base = this.resolveApiBase();
        const jobPath = this.jobPath();
        if (!jobPath) {
            console.warn('[RegWizard] loadConfirmation skipped: no jobPath');
            return;
        }
        this.http.get<PlayerRegConfirmationDto>(`${base}/player-registration/confirmation`)
            .subscribe({
                next: dto => this._confirmation.set(dto),
                error: err => {
                    console.warn('[RegWizard] Confirmation fetch failed', err);
                    this._confirmation.set(null);
                }
            });
    }

    /** Trigger server to re-send the confirmation email to the family user's email address. */
    resendConfirmationEmail(): Promise<boolean> {
        const base = this.resolveApiBase();
        return firstValueFrom(this.http.post(`${base}/player-registration/confirmation/resend`, null))
            .then(() => true)
            .catch(err => {
                console.warn('[RegWizard] Resend confirmation failed', err);
                return false;
            });
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
        const opts = this.getJobOptionsObject() as Record<string, unknown> | null;
        const v = opts ? (opts['USLaxNumberValidThroughDate'] ?? opts['usLaxNumberValidThroughDate'] ?? null) : null;
        if (!v) return null;
        const d = new Date(v as string | number);
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
            const d = new Date(vals[pick] as string | number);
            if (!Number.isNaN(d.getTime())) return d;
        }
        return null;
    }

    togglePlayerSelection(player: string | { playerId: string; registered?: boolean; selected?: boolean }): void {
        const playerId = typeof player === 'string' ? player : player?.playerId;
        if (!playerId) return;

        const becameSelected = this.togglePlayerInList(playerId);
        this.recomputeWaiverAcceptanceOnSelectionChange();

        if (becameSelected && this.profileFieldSchemas().length) {
            this.initializePlayerFormDefaults(playerId);
        }
    }

    private togglePlayerInList(playerId: string): boolean {
        const list = this.familyPlayers();
        let becameSelected = false;

        this._familyPlayers.set(list.map(p => {
            if (p.playerId !== playerId) return p;
            if (p.registered) return p; // locked
            const nextSelected = !p.selected;
            if (nextSelected && !p.selected) becameSelected = true;
            return { ...p, selected: nextSelected };
        }));

        return becameSelected;
    }

    private initializePlayerFormDefaults(playerId: string): void {
        try {
            const schemas = this.profileFieldSchemas();
            this.initializeFormFieldsForPlayer(playerId, schemas);
            this.applyDefaultValuesForPlayer(playerId, schemas);
            this.applyEligibilityFromDefaults(playerId, schemas);
        } catch { /* ignore */ }
    }

    private initializeFormFieldsForPlayer(playerId: string, schemas: PlayerProfileFieldSchema[]): void {
        const current = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        if (!current[playerId]) current[playerId] = {};
        for (const f of schemas) {
            if (!(f.name in current[playerId])) {
                current[playerId][f.name] = null;
            }
        }
        this._playerFormValues.set(current);
    }

    private applyDefaultValuesForPlayer(playerId: string, schemas: PlayerProfileFieldSchema[]): void {
        const fam = this.familyPlayers().find(p => p.playerId === playerId);
        if (!fam || fam.registered || !fam.defaultFieldValues) return;

        const df = fam.defaultFieldValues;
        const schemaNameByLower: Record<string, string> = {};
        for (const s of schemas) schemaNameByLower[s.name.toLowerCase()] = s.name;

        const alias = this.formSchema.aliasFieldMap();
        const curVals = { ...this.playerFormValues() } as Record<string, Record<string, PlayerFormFieldValue>>;
        const target = curVals[playerId];

        for (const [rawK, rawV] of Object.entries(df)) {
            if (rawV == null || rawV === '') continue;

            const targetName = this.resolveFieldName(rawK, schemaNameByLower, alias);
            if (!targetName) continue;

            const existing = target[targetName];
            const isBlank = this.isFieldValueBlank(existing);
            if (isBlank) target[targetName] = rawV as PlayerFormFieldValue;
        }

        this._playerFormValues.set(curVals);
    }



    private isFieldValueBlank(value: unknown): boolean {
        return value == null ||
            (typeof value === 'string' && value.trim() === '') ||
            (Array.isArray(value) && value.length === 0);
    }

    private applyEligibilityFromDefaults(playerId: string, schemas: PlayerProfileFieldSchema[]): void {
        try {
            const eligField = this.determineEligibilityField(schemas) || this.resolveEligibilityFieldNameFromSchemas();
            if (!eligField) return;

            const curVals = this.playerFormValues();
            const rawElig = curVals[playerId]?.[eligField];
            if (rawElig == null || String(rawElig).trim() === '') return;

            const existingElig = this.getEligibilityForPlayer(playerId);
            if (existingElig && String(existingElig).trim() !== '') return;

            this.playerState.setEligibilityForPlayer(playerId, String(rawElig).trim());
            this.updateUnifiedTeamConstraint();
        } catch { /* ignore */ }
    }

    private updateUnifiedTeamConstraint(): void {
        const selectedIds = this.selectedPlayerIds();
        const eligValues = selectedIds
            .map(id => this.getEligibilityForPlayer(id))
            .filter(v => !!v);
        const uniq = Array.from(new Set(eligValues));
        if (uniq.length === 1) this._teamConstraintValue.set(uniq[0]!);
    }

    selectedPlayerIds(): string[] {
        return this.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId);
    }

    // Deprecated adapters removed: components now derive directly from familyPlayers/familyUser.


    setUsLaxValidating(playerId: string): void {
        this.updateUsLaxEntry(playerId, { status: 'validating', message: undefined });
    }
    setUsLaxResult(playerId: string, ok: boolean, message?: string, membership?: Record<string, unknown>): void {
        this.updateUsLaxEntry(playerId, { status: ok ? 'valid' : 'invalid', message, membership });
    }
    private updateUsLaxEntry(playerId: string, patch: Partial<{ status: 'idle' | 'validating' | 'valid' | 'invalid'; message?: string; membership?: Record<string, unknown> }>): void {
        const m = { ...this.usLaxStatus() };
        const cur = m[playerId] || { value: '', status: 'idle' as const };
        m[playerId] = { ...cur, ...patch };
        this._usLaxStatus.set(m);
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
        if (tctype === 'BYGRADYEAR') {
            if (hasAllParts(lname, ['grad', 'year']) || hasAllParts(llabel, ['grad', 'year'])) return false;
        } else if (tctype === 'BYAGEGROUP') {
            if (hasAllParts(lname, ['age', 'group']) || hasAllParts(llabel, ['age', 'group'])) return false;
        } else if (tctype === 'BYAGERANGE') {
            if (hasAllParts(lname, ['age', 'range']) || hasAllParts(llabel, ['age', 'range'])) return false;
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

    private isRequiredInvalid(field: PlayerProfileFieldSchema, raw: unknown, str: string): boolean {
        if (!field.required) return false;
        if (field.type === 'checkbox') return raw !== true;
        if (field.type === 'multiselect') {
            return !Array.isArray(raw) || raw.length === 0;
        }
        return str.length === 0;
    }

    private validateBasicType(field: PlayerProfileFieldSchema, raw: unknown, str: string): string | null {
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
    condition?: { field: string; value: unknown; operator?: string } | null;
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

/** Allowed form field values (covers text, number, date, checkbox, select, multiselect). */
export type PlayerFormFieldValue = string | number | boolean | null | string[];

// JSON type helper to avoid `any`/`unknown` in public fields
type Json = string | number | boolean | null | Json[] | { [key: string]: Json };
