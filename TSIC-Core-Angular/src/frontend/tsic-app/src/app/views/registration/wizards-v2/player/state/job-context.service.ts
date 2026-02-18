import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { FormSchemaService } from '@views/registration/wizards/player-registration-wizard/services/form-schema.service';
import { WaiverStateService } from '@views/registration/wizards/player-registration-wizard/services/waiver-state.service';
import { getPropertyCI } from '@views/registration/wizards/shared/utils/property-utils';
import type { Loadable } from '@infrastructure/shared/state.models';
import type {
    JobMetadataResponse,
    VIPlayerObjectResponse,
    PaymentOption,
    PlayerProfileFieldSchema,
    WaiverDefinition,
    FamilyPlayerDto,
    PreSubmitValidationErrorDto,
    Json,
} from '../types/player-wizard.types';

/**
 * Job Context Service — owns job identity, metadata, payment config, and field schemas.
 *
 * Extracted from RegistrationWizardService lines ~37-41, 102-120, 460-532, 928-938.
 * Gold-standard signal pattern: private backing + public readonly + controlled mutators.
 */
@Injectable({ providedIn: 'root' })
export class JobContextService {
    private readonly http = inject(HttpClient);
    private readonly destroyRef = inject(DestroyRef);
    private readonly formSchema = inject(FormSchemaService);
    private readonly waiverState = inject(WaiverStateService);

    // ── Job identity ──────────────────────────────────────────────────
    private readonly _jobPath = signal('');
    private readonly _jobId = signal('');
    readonly jobPath = this._jobPath.asReadonly();
    readonly jobId = this._jobId.asReadonly();

    // ── Raw metadata JSON ─────────────────────────────────────────────
    private readonly _jobProfileMetadataJson = signal<string | null>(null);
    private readonly _jobJsonOptions = signal<string | null>(null);
    readonly jobProfileMetadataJson = this._jobProfileMetadataJson.asReadonly();
    readonly jobJsonOptions = this._jobJsonOptions.asReadonly();

    // ── Payment flags & ARB schedule ──────────────────────────────────
    private readonly _adnArb = signal(false);
    private readonly _adnArbBillingOccurences = signal<number | null>(null);
    private readonly _adnArbIntervalLength = signal<number | null>(null);
    private readonly _adnArbStartDate = signal<string | null>(null);
    readonly adnArb = this._adnArb.asReadonly();
    readonly adnArbBillingOccurences = this._adnArbBillingOccurences.asReadonly();
    readonly adnArbIntervalLength = this._adnArbIntervalLength.asReadonly();
    readonly adnArbStartDate = this._adnArbStartDate.asReadonly();

    // ── Discount/Amex flags (from /family/players response) ───────────
    private readonly _jobHasActiveDiscountCodes = signal(false);
    private readonly _jobUsesAmex = signal(false);
    readonly jobHasActiveDiscountCodes = this._jobHasActiveDiscountCodes.asReadonly();
    readonly jobUsesAmex = this._jobUsesAmex.asReadonly();

    // ── Insurance ─────────────────────────────────────────────────────
    private readonly _offerPlayerRegSaver = signal(false);
    private readonly _verticalInsureOffer = signal<Loadable<VIPlayerObjectResponse>>({ loading: false, data: null, error: null });
    readonly offerPlayerRegSaver = this._offerPlayerRegSaver.asReadonly();
    readonly verticalInsureOffer = this._verticalInsureOffer.asReadonly();

    // ── Payment option ────────────────────────────────────────────────
    private readonly _paymentOption = signal<PaymentOption>('PIF');
    readonly paymentOption = this._paymentOption.asReadonly();

    // ── Parsed field schemas ──────────────────────────────────────────
    private readonly _profileFieldSchemas = signal<PlayerProfileFieldSchema[]>([]);
    private readonly _aliasFieldMap = signal<Record<string, string>>({});
    readonly profileFieldSchemas = this._profileFieldSchemas.asReadonly();
    readonly aliasFieldMap = this._aliasFieldMap.asReadonly();

    // ── Waivers (job-level HTML blocks) ───────────────────────────────
    private readonly _jobWaivers = signal<Record<string, string>>({});
    readonly jobWaivers = this._jobWaivers.asReadonly();

    // ── Server validation errors (from preSubmit) ─────────────────────
    private _serverValidationErrors: PreSubmitValidationErrorDto[] | undefined;

    // ── Lazy-parsed options cache ─────────────────────────────────────
    private parsedJobOptions: Json | null | undefined;

    // ── Controlled mutators ───────────────────────────────────────────
    setJobPath(v: string): void { this._jobPath.set(v); }
    setJobId(v: string): void { this._jobId.set(v); }
    setPaymentOption(v: PaymentOption): void { this._paymentOption.set(v); }
    setVerticalInsureOffer(v: Loadable<VIPlayerObjectResponse>): void { this._verticalInsureOffer.set(v); }

    setPaymentFlags(hasDiscount: boolean, usesAmex: boolean): void {
        this._jobHasActiveDiscountCodes.set(!!hasDiscount);
        this._jobUsesAmex.set(!!usesAmex);
    }

    setServerValidationErrors(errors: PreSubmitValidationErrorDto[] | undefined): void {
        this._serverValidationErrors = errors;
    }
    getServerValidationErrors(): PreSubmitValidationErrorDto[] {
        return this._serverValidationErrors ? [...this._serverValidationErrors] : [];
    }
    hasServerValidationErrors(): boolean {
        return !!this._serverValidationErrors?.length;
    }

    // ── API: load job metadata ────────────────────────────────────────
    /**
     * Fetch job metadata if not already loaded. Parses profile metadata JSON,
     * extracts ARB payment schedule, waiver definitions, and field schemas.
     *
     * The selectedPlayerIds and familyPlayers are passed in for waiver seeding
     * (they come from FamilyPlayersService — avoids circular dependency).
     */
    ensureJobMetadata(
        jobPath: string,
        selectedPlayerIds: string[],
        familyPlayers: FamilyPlayerDto[],
    ): void {
        if (!jobPath) return;
        if (this._jobProfileMetadataJson() && this._jobJsonOptions()) return;
        const base = this.resolveApiBase();
        this.http.get<JobMetadataResponse>(`${base}/jobs/${encodeURIComponent(jobPath)}`)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: meta => {
                    this._jobId.set(meta.jobId);
                    this._jobProfileMetadataJson.set(meta.playerProfileMetadataJson || null);
                    this._jobJsonOptions.set(meta.jsonOptions || null);
                    this.parsedJobOptions = undefined; // invalidate cache

                    // ARB payment schedule
                    const m = meta as Record<string, unknown>;
                    const arb = getPropertyCI<boolean>(m, 'adnArb') ?? false;
                    const occ = getPropertyCI<number>(m, 'adnArbBillingOccurences') ?? null;
                    const intLen = getPropertyCI<number>(m, 'adnArbIntervalLength') ?? null;
                    const start = getPropertyCI<string>(m, 'adnArbStartDate') ?? null;
                    this._adnArb.set(!!arb);
                    this._adnArbBillingOccurences.set(typeof occ === 'number' ? occ : null);
                    this._adnArbIntervalLength.set(typeof intLen === 'number' ? intLen : null);
                    this._adnArbStartDate.set(start ? String(start) : null);
                    this._paymentOption.set(this._adnArb() ? 'ARB' : 'PIF');

                    // RegSaver insurance offer flag
                    const offer = getPropertyCI<boolean>(m, 'offerPlayerRegsaverInsurance');
                    this._offerPlayerRegSaver.set(!!offer);

                    // Waiver extraction via WaiverStateService
                    const waivers = this.waiverState.buildFromMetadata(
                        meta,
                        this._jobProfileMetadataJson(),
                        selectedPlayerIds,
                        familyPlayers,
                    );
                    this._jobWaivers.set(waivers);

                    // Parse profile field schemas
                    this.parseProfileMetadata(selectedPlayerIds, familyPlayers);
                },
                error: (err: unknown) => {
                    console.error('[JobContext] Failed to load job metadata', err);
                },
            });
    }

    // ── Metadata parsing ──────────────────────────────────────────────
    /**
     * Called after metadata is loaded. Delegates to FormSchemaService for JSON
     * parsing, then triggers schema binding. The actual form value initialization
     * is handled by the caller (PlayerFormsService) via a callback pattern.
     */
    parseProfileMetadata(selectedPlayerIds: string[], familyPlayers: FamilyPlayerDto[]): void {
        this.formSchema.parse(this._jobProfileMetadataJson(), this._jobJsonOptions());
        const schemas = this.formSchema.profileFieldSchemas();
        this._profileFieldSchemas.set(schemas);
        this._aliasFieldMap.set(this.formSchema.aliasFieldMap());
        this.bindWaiversToSchemas(schemas, selectedPlayerIds, familyPlayers);
    }

    private bindWaiversToSchemas(
        schemas: PlayerProfileFieldSchema[],
        selectedPlayerIds: string[],
        familyPlayers: FamilyPlayerDto[],
    ): void {
        this.waiverState.processSchemasAndBindWaivers(
            this.waiverState.waiverDefinitions(),
            schemas.map(s => ({ name: s.name, label: s.label, type: s.type, required: s.required, visibility: s.visibility })),
            selectedPlayerIds,
            familyPlayers,
        );
    }

    // ── US Lax valid-through date ─────────────────────────────────────
    getUsLaxValidThroughDate(): Date | null {
        const opts = this.getJobOptionsObject() as Record<string, unknown> | null;
        const v = opts ? (opts['USLaxNumberValidThroughDate'] ?? opts['usLaxNumberValidThroughDate'] ?? null) : null;
        if (!v) return null;
        const d = new Date(v as string | number);
        return Number.isNaN(d.getTime()) ? null : d;
    }

    private getJobOptionsObject(): Json | null {
        if (this.parsedJobOptions !== undefined) return this.parsedJobOptions;
        const raw = this._jobJsonOptions();
        if (!raw) { this.parsedJobOptions = null; return null; }
        try { this.parsedJobOptions = JSON.parse(raw) as Json; }
        catch { this.parsedJobOptions = null; }
        return this.parsedJobOptions ?? null;
    }

    // ── Insurance offer processing (from preSubmit response) ──────────
    processInsuranceOffer(resp: Record<string, unknown>): void {
        try {
            const ins = getPropertyCI<{ available?: boolean; playerObject?: unknown; error?: unknown }>(resp, 'insurance');
            if (ins?.available && ins?.playerObject) {
                this._verticalInsureOffer.set({ loading: false, data: ins.playerObject, error: null });
            } else if (ins?.error) {
                this._verticalInsureOffer.set({ loading: false, data: null, error: String(ins.error) });
            } else {
                this._verticalInsureOffer.set({ loading: false, data: null, error: null });
            }
        } catch { /* ignore */ }
    }

    // ── API base resolution ───────────────────────────────────────────
    resolveApiBase(): string {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost') || host.startsWith('127.0.0.1')) {
                return 'https://localhost:7215/api';
            }
        } catch { /* SSR or no window */ }
        return environment.apiUrl.endsWith('/api') ? environment.apiUrl : `${environment.apiUrl}/api`;
    }

    // ── Waiver facades ────────────────────────────────────────────────
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
    setWaiverAccepted(idOrField: string, accepted: boolean): void { this.waiverState.setWaiverAccepted(idOrField, accepted); }
    isWaiverAccepted(key: string): boolean { return this.waiverState.isWaiverAccepted(key); }
    allRequiredWaiversAccepted(): boolean { return this.waiverState.allRequiredWaiversAccepted(); }
    requireSignature(): boolean { return this.waiverState.requireSignature(); }

    recomputeWaiverAcceptanceOnSelectionChange(selectedPlayerIds: string[], familyPlayers: FamilyPlayerDto[]): void {
        try { this.waiverState.recomputeWaiverAcceptanceOnSelectionChange(selectedPlayerIds, familyPlayers); }
        catch { /* ignore */ }
    }

    // ── Reset ─────────────────────────────────────────────────────────
    reset(): void {
        this._jobPath.set('');
        this._jobId.set('');
        this._jobProfileMetadataJson.set(null);
        this._jobJsonOptions.set(null);
        this.parsedJobOptions = undefined;
        this._adnArb.set(false);
        this._adnArbBillingOccurences.set(null);
        this._adnArbIntervalLength.set(null);
        this._adnArbStartDate.set(null);
        this._jobHasActiveDiscountCodes.set(false);
        this._jobUsesAmex.set(false);
        this._offerPlayerRegSaver.set(false);
        this._verticalInsureOffer.set({ loading: false, data: null, error: null });
        this._paymentOption.set('PIF');
        this._profileFieldSchemas.set([]);
        this._aliasFieldMap.set({});
        this._jobWaivers.set({});
        this._serverValidationErrors = undefined;
    }
}
