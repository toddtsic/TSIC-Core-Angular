import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { getPropertyCI } from '@views/registration/wizards/shared/utils/property-utils';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import { EligibilityService } from './eligibility.service';
import { PlayerFormsService } from './player-forms.service';
import type {
    FamilyPlayersResponseDto,
    FamilyPlayerDto,
    PreSubmitPlayerRegistrationRequestDto,
    PreSubmitPlayerRegistrationResponseDto,
    PreSubmitTeamSelectionDto,
    PreSubmitValidationErrorDto,
    PlayerRegConfirmationDto,
    PaymentSummary,
    Json,
} from '../types/player-wizard.types';

/**
 * Player Wizard State Service — THIN ORCHESTRATOR.
 *
 * Owns nothing of its own except the confirmation/lastPayment signals.
 * Coordinates across JobContextService, FamilyPlayersService,
 * EligibilityService, and PlayerFormsService for cross-cutting operations.
 *
 * ~100 lines. Replaces the 1,314-line monolith.
 */
@Injectable({ providedIn: 'root' })
export class PlayerWizardStateService {
    private readonly http = inject(HttpClient);
    private readonly destroyRef = inject(DestroyRef);

    readonly jobCtx = inject(JobContextService);
    readonly familyPlayers = inject(FamilyPlayersService);
    readonly eligibility = inject(EligibilityService);
    readonly playerForms = inject(PlayerFormsService);

    // ── Orchestrator-owned signals ────────────────────────────────────
    private readonly _lastPayment = signal<PaymentSummary | null>(null);
    private readonly _confirmation = signal<PlayerRegConfirmationDto | null>(null);
    readonly lastPayment = this._lastPayment.asReadonly();
    readonly confirmation = this._confirmation.asReadonly();

    // ── Controlled mutators ───────────────────────────────────────────
    setLastPayment(v: PaymentSummary | null): void { this._lastPayment.set(v); }
    setConfirmation(v: PlayerRegConfirmationDto | null): void { this._confirmation.set(v); }

    // ── Initialize ────────────────────────────────────────────────────
    /**
     * Main entry point. Called when the wizard mounts or the jobPath changes.
     * Loads family players, then chains job metadata + form initialization.
     */
    initialize(jobPath: string): void {
        const apiBase = this.jobCtx.resolveApiBase();
        this.jobCtx.setJobPath(jobPath);

        this.familyPlayers.loadFamilyPlayers(jobPath, apiBase, (resp, players) => {
            this.onFamilyPlayersLoaded(resp, players, jobPath);
        });
    }

    private onFamilyPlayersLoaded(
        resp: FamilyPlayersResponseDto,
        players: FamilyPlayerDto[],
        jobPath: string,
    ): void {
        // Extract constraint type
        const ct = this.familyPlayers.extractConstraintType(resp);
        if (ct) this.eligibility.setTeamConstraintType(ct);

        // Prefill teams from prior registrations
        this.familyPlayers.prefillTeamsFromPriorRegistrations(
            players,
            this.eligibility.selectedTeams(),
            map => this.eligibility.setSelectedTeams(map),
        );

        // Payment flags from response
        this.jobCtx.setPaymentFlags(!!resp.jobHasActiveDiscountCodes, !!resp.jobUsesAmex);

        // Load job metadata (triggers form schema parsing)
        const selectedIds = this.familyPlayers.selectedPlayerIds();
        this.jobCtx.ensureJobMetadata(jobPath, selectedIds, players);

        // After metadata loads, initialize form values
        // (ensureJobMetadata is async — form init happens in its subscribe callback via parseProfileMetadata)
        // We also need to trigger form init from here for when metadata is already cached
        const schemas = this.jobCtx.profileFieldSchemas();
        if (schemas.length > 0) {
            this.initializeFormsFromSchemas(schemas, selectedIds, players);
        }
    }

    /** Called when schemas are ready (either from cache or after metadata load). */
    initializeFormsFromSchemas(
        schemas: import('../types/player-wizard.types').PlayerProfileFieldSchema[],
        selectedIds: string[],
        players: FamilyPlayerDto[],
    ): void {
        this.playerForms.initializeFormValuesForSelectedPlayers(schemas, selectedIds);
        this.playerForms.seedFromPriorRegistrations(schemas, players);
        this.playerForms.seedFromDefaults(schemas, players);
        this.playerForms.applyAliasBackfill();
        this.eligibility.seedEligibilityFromSchemas(
            schemas, players, selectedIds,
            (pid, field) => this.playerForms.getPlayerFieldValue(pid, field),
        );
    }

    // ── Player toggle ─────────────────────────────────────────────────
    togglePlayerSelection(playerId: string): void {
        const becameSelected = this.familyPlayers.togglePlayerSelection(playerId);
        const selectedIds = this.familyPlayers.selectedPlayerIds();
        const players = this.familyPlayers.familyPlayers();
        this.jobCtx.recomputeWaiverAcceptanceOnSelectionChange(selectedIds, players);

        if (becameSelected) {
            const schemas = this.jobCtx.profileFieldSchemas();
            if (schemas.length) {
                this.playerForms.initializePlayerFormDefaults(playerId, schemas, players);
                this.eligibility.applyEligibilityFromDefaults(
                    playerId, schemas,
                    (pid, field) => this.playerForms.getPlayerFieldValue(pid, field),
                    selectedIds,
                );
            }
        }
    }

    /** Remove form values & team selections for deselected players. */
    pruneDeselectedPlayers(): void {
        const selectedIds = new Set(this.familyPlayers.selectedPlayerIds());
        this.playerForms.pruneDeselectedPlayers(selectedIds);
        this.eligibility.pruneDeselectedTeams(selectedIds);
    }

    // ── PreSubmit ─────────────────────────────────────────────────────
    async preSubmitRegistration(): Promise<PreSubmitPlayerRegistrationResponseDto> {
        const jobPath = this.jobCtx.jobPath();
        const familyUserId = this.familyPlayers.familyUser()?.familyUserId;
        if (!jobPath || !familyUserId) {
            throw new Error('Missing jobPath or familyUserId');
        }
        const payload = this.buildPreSubmitPayload(jobPath);
        const apiBase = this.jobCtx.resolveApiBase();
        const resp = await firstValueFrom(
            this.http.post<PreSubmitPlayerRegistrationResponseDto>(`${apiBase}/player-registration/preSubmit`, payload),
        );
        this.captureServerValidationErrors(resp);
        this.jobCtx.processInsuranceOffer(resp as Record<string, unknown>);
        if (!resp) throw new Error('No response from preSubmit API');
        return resp;
    }

    private buildPreSubmitPayload(jobPath: string): PreSubmitPlayerRegistrationRequestDto {
        const schemas = this.jobCtx.profileFieldSchemas();
        const waiverFieldNames = this.jobCtx.waiverFieldNames();
        const waiversGateOk = this.jobCtx.waiversGateOk();
        const teamSelections: PreSubmitTeamSelectionDto[] = [];

        for (const pid of this.familyPlayers.selectedPlayerIds()) {
            const teamId = this.eligibility.selectedTeams()[pid];
            if (!teamId) continue;
            const formValues = this.playerForms.buildPreSubmitFormValuesForPlayer(
                pid, schemas, waiverFieldNames, waiversGateOk,
                key => this.jobCtx.isWaiverAccepted(key),
                p => this.eligibility.getEligibilityForPlayer(p),
                () => this.eligibility.determineEligibilityField(schemas),
            );
            this.injectRequiredTeamField(formValues, teamId, schemas);
            if (Array.isArray(teamId)) {
                for (const tid of teamId) teamSelections.push({ playerId: pid, teamId: tid, formValues });
            } else {
                teamSelections.push({ playerId: pid, teamId, formValues });
            }
        }
        return { jobPath, teamSelections };
    }

    private injectRequiredTeamField(
        formValues: { [key: string]: Json },
        teamId: string | string[],
        schemas: import('../types/player-wizard.types').PlayerProfileFieldSchema[],
    ): void {
        const schema = schemas.find(s => s.name.toLowerCase() === 'teamid');
        if (!schema?.required) return;
        const existing = Object.entries(formValues).find(([k]) => k.toLowerCase() === 'teamid');
        if (existing && typeof existing[1] === 'string' && existing[1].trim()) return;
        if (typeof teamId === 'string') formValues['teamId'] = teamId;
        else if (Array.isArray(teamId) && teamId.length === 1 && typeof teamId[0] === 'string') formValues['teamId'] = teamId[0];
    }

    private captureServerValidationErrors(resp: PreSubmitPlayerRegistrationResponseDto): void {
        try {
            const ve = getPropertyCI<PreSubmitValidationErrorDto[]>(resp as Record<string, unknown>, 'validationErrors');
            this.jobCtx.setServerValidationErrors((ve && Array.isArray(ve) && ve.length) ? ve : []);
        } catch {
            this.jobCtx.setServerValidationErrors([]);
        }
    }

    // ── Confirmation ──────────────────────────────────────────────────
    loadConfirmation(): void {
        const apiBase = this.jobCtx.resolveApiBase();
        if (!this.jobCtx.jobPath()) return;
        this.http.get<PlayerRegConfirmationDto>(`${apiBase}/player-registration/confirmation`)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: dto => this._confirmation.set(dto),
                error: err => {
                    console.warn('[PlayerWizard] Confirmation fetch failed', err);
                    this._confirmation.set(null);
                },
            });
    }

    async resendConfirmationEmail(): Promise<boolean> {
        const apiBase = this.jobCtx.resolveApiBase();
        try {
            await firstValueFrom(this.http.post(`${apiBase}/player-registration/confirmation/resend`, null));
            return true;
        } catch (err: unknown) {
            console.warn('[PlayerWizard] Resend confirmation failed', err);
            return false;
        }
    }

    // ── Helper: get last name with form value fallback ─────────────────
    getPlayerLastName(playerId: string): string | null {
        const fam = this.familyPlayers.getPlayerLastName(playerId);
        if (fam) return fam;
        const vals = this.playerForms.playerFormValues()[playerId] || {};
        const keys = Object.keys(vals);
        const pick = keys.find(k => k.toLowerCase() === 'lastname')
            || keys.find(k => k.toLowerCase().includes('last') && k.toLowerCase().includes('name'))
            || null;
        return pick ? String(vals[pick] ?? '').trim() || null : null;
    }

    /** DOB with form value fallback. */
    getPlayerDob(playerId: string): Date | null {
        const fam = this.familyPlayers.getPlayerDob(playerId);
        if (fam) return fam;
        const vals = this.playerForms.playerFormValues()[playerId] || {};
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

    // ── Reset ─────────────────────────────────────────────────────────
    reset(): void {
        this.jobCtx.reset();
        this.familyPlayers.reset();
        this.eligibility.reset();
        this.playerForms.reset();
        this._lastPayment.set(null);
        this._confirmation.set(null);
    }

    /** Reset family-specific state but preserve job context. */
    resetForFamilySwitch(): void {
        const jp = this.jobCtx.jobPath();
        const jid = this.jobCtx.jobId();
        this.reset();
        if (jp) this.jobCtx.setJobPath(jp);
        if (jid) this.jobCtx.setJobId(jid);
    }
}
