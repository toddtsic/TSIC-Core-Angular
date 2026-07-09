import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import { getPropertyCI } from '@views/registration/shared/utils/property-utils';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import { EligibilityService } from './eligibility.service';
import { PlayerFormsService } from './player-forms.service';
import { InsuranceStateV2Service } from './insurance-state-v2.service';
import { InsuranceV2Service } from './insurance-v2.service';
import { TeamService } from '@views/registration/player/services/team.service';
import type {
    AvailableTeamDto,
    FamilyPlayersResponseDto,
    FamilyPlayerDto,
    PlayerProfileFieldSchema,
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
    private readonly teamService = inject(TeamService);
    private readonly insuranceState = inject(InsuranceStateV2Service);
    private readonly insuranceSvc = inject(InsuranceV2Service);

    // ── Field visibility chokepoint ───────────────────────────────────
    /**
     * Single source of truth for "is this field visible for this player" across
     * render, validation, field-error display, and review. Resolves the recruiting
     * gate context (List_RecruitingGradYears + the registered team's grad year) here
     * so every caller gates identically — no per-call-site drift.
     */
    isFieldVisibleForPlayer(playerId: string, field: PlayerProfileFieldSchema): boolean {
        const wfn = this.jobCtx.waiverFieldNames();
        const tct = this.eligibility.teamConstraintType();
        const recruitingGradYears = this.jobCtx.recruitingGradYears();
        const teamGradYear = this.resolveTeamGradYear(playerId, recruitingGradYears);
        return this.playerForms.isFieldVisibleForPlayer(
            playerId, field, wfn, tct, recruitingGradYears, teamGradYear,
        );
    }

    /**
     * Grad year of the team the player is registering for, used to gate the
     * College Recruiting fields. Returns the recruiting grad year the selected
     * team's agegroup/name matches (e.g. "2028"), or null if none — which hides
     * the recruiting fields. Reuses the same agegroup/team-name match the team
     * filter uses for BYGRADYEAR (team.service.ts). Mirrors legacy
     * AdjustRecruittingInfoVisibility (registration grad year, no job-type gate).
     */
    private resolveTeamGradYear(playerId: string, recruitingGradYears: string[]): string | null {
        if (recruitingGradYears.length === 0) return null;
        const teamId = this.eligibility.selectedTeams()[playerId]?.[0];
        if (!teamId) return null;
        const team = this.teamService.getTeamById(teamId);
        if (!team) return null;
        const hay = `${team.agegroupName ?? ''} ${team.teamName ?? ''}`.toLowerCase();
        return recruitingGradYears.find(yr => hay.includes(yr.toLowerCase())) ?? null;
    }

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
     * Main entry point. Called when the wizard mounts or the jobPath changes. Owns the two
     * independent async loads the wizard needs — family players (→ prefilled selectedTeams) and
     * available teams (→ agegroup names) — so it can coordinate them in one place.
     */
    initialize(jobPath: string): void {
        const apiBase = this.jobCtx.resolveApiBase();
        this.jobCtx.setJobPath(jobPath);

        // The BYAGEGROUP resume backfill needs BOTH loads complete. Gate it behind a two-signal
        // barrier and fire it exactly once, when the second of the two finishes — deterministic,
        // single trigger, no Angular effect. Flags are closure-scoped so each initialize() call
        // (e.g. on jobPath change) starts fresh.
        let familyLoaded = false;
        let teamsLoaded = false;
        const backfillAgegroupWhenReady = () => {
            if (familyLoaded && teamsLoaded) this.backfillAgegroupEligibilityFromTeams();
        };

        this.familyPlayers.loadFamilyPlayers(jobPath, apiBase, (resp, players) => {
            this.onFamilyPlayersLoaded(resp, players, jobPath);
            familyLoaded = true;
            backfillAgegroupWhenReady();
        });
        this.teamService.loadForJob(jobPath, () => {
            teamsLoaded = true;
            backfillAgegroupWhenReady();
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

        // Register callback so form init runs when schemas arrive (async metadata fetch).
        // Read selectedIds and players fresh from signals — the values captured at
        // ensureJobMetadata call time may be stale (players not yet selected).
        this.jobCtx.setSchemasReadyCallback((schemas) => {
            const freshIds = this.familyPlayers.selectedPlayerIds();
            const freshPlayers = this.familyPlayers.familyPlayers();
            this.initializeFormsFromSchemas(schemas, freshIds, freshPlayers);
        });

        this.jobCtx.ensureJobMetadata(jobPath, selectedIds, players);

        // If schemas are already cached (metadata was loaded in a prior call),
        // initialize immediately — the callback won't fire since ensureJobMetadata
        // short-circuits when metadata is already present.
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
        // console.warn(`[initForms] selectedIds=${selectedIds.length} players=${players.length} schemas=${schemas.length}`,
        //     players.map(p => ({ id: p.playerId.slice(0,8), reg: p.registered, sel: p.selected })));
        this.playerForms.initializeFormValuesForSelectedPlayers(schemas, selectedIds);
        this.playerForms.seedFromPriorRegistrations(schemas, players);
        this.playerForms.seedFromDefaults(schemas, players);
        this.playerForms.applyAliasBackfill();
        this.playerForms.clearInvalidSelectValues(schemas);
        this.eligibility.seedEligibilityFromSchemas(
            schemas, players, selectedIds,
            (pid, field) => this.playerForms.getPlayerFieldValue(pid, field),
        );
    }

    /**
     * Sets BYAGEGROUP eligibility from the assigned team's CURRENT agegroup name, read from the
     * loaded teams list — the exact same source the dropdown options come from, so the seeded
     * value is guaranteed to match an option (the agegroup NAME is mutable; the team id is the
     * stable fact, which is why we resolve through the team rather than store a name). The assigned
     * team is prefilled into selectedTeams from the active/pending prior reg.
     *
     * Invoked once by initialize()'s barrier, after BOTH the family and teams loads complete (no
     * Angular effect, no race). Fill-empty: never overrides an existing value or a user selection.
     */
    backfillAgegroupEligibilityFromTeams(): void {
        if ((this.eligibility.teamConstraintType() || '').toUpperCase() !== 'BYAGEGROUP') return;
        const teams = this.teamService.allTeams();
        if (!teams || teams.length === 0) return;
        const selTeams = this.eligibility.selectedTeams();
        const pids = Object.keys(selTeams);
        if (pids.length === 0) return;
        let changed = false;
        for (const pid of pids) {
            if (this.eligibility.getEligibilityForPlayer(pid)) continue; // never override
            const teamId = selTeams[pid]?.[0];
            if (!teamId) continue;
            const agName = this.teamService.getTeamById(teamId)?.agegroupName?.trim();
            if (agName) { this.eligibility.setEligibilityForPlayer(pid, agName); changed = true; }
        }
        if (changed) this.eligibility.updateUnifiedConstraintValue(this.familyPlayers.selectedPlayerIds());
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

    // ── PreSubmit (review → payment): creates the registrations ──────────
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
        // Re-reflect the fresh post-reconcile team snapshot so agegroup options / team list / defaults
        // track current occupancy instead of stale init-time data.
        this.applyTeamsFromRoundTrip(resp.rawTeams);
        return resp;
    }

    /**
     * Push a round-trip's raw team snapshot into the TeamService dataset and re-run the BYAGEGROUP
     * backfill so every team-derived field reflects the new data. Reconcile, not reset: the
     * backfill is fill-empty-only, so it never clobbers an in-progress eligibility selection.
     */
    private applyTeamsFromRoundTrip(rawTeams: unknown): void {
        if (!rawTeams || !Array.isArray(rawTeams)) return;
        this.teamService.applyRawTeams(rawTeams as AvailableTeamDto[]);
        this.backfillAgegroupEligibilityFromTeams();
    }

    /**
     * Re-point each player's team selection at the team their CURRENT (reloaded) registration is
     * on — the same prior-reg prefill the initial family load runs (onFamilyPlayersLoaded), re-run
     * against the freshly reloaded players. Call this AFTER the post-PreSubmit family reload.
     *
     * PreSubmit's seat reconcile moves a player whose team filled up to the $0 WAITLIST twin
     * (active reg on the twin), but selectedTeams still names the now-full real team. The payment
     * table keys each line off selectedTeams, so without this it finds no reg on the real team and
     * re-bills the full real-team fee (the "both on the regular team, owing the sum" symptom).
     * prefill is merge + fill-from-usable-priors, so it re-points the moved player to the twin and
     * leaves the seated sibling on the real team. (Plan item #5: reconcile, don't reset.)
     */
    reconcileSelectionsFromCurrentRegistrations(): void {
        this.familyPlayers.prefillTeamsFromPriorRegistrations(
            this.familyPlayers.familyPlayers(),
            this.eligibility.selectedTeams(),
            map => this.eligibility.setSelectedTeams(map),
        );
    }

    private buildPreSubmitPayload(jobPath: string): PreSubmitPlayerRegistrationRequestDto {
        const schemas = this.jobCtx.profileFieldSchemas();
        const waiverFieldNames = this.jobCtx.waiverFieldNames();
        const waiversGateOk = this.jobCtx.waiversGateOk();
        const teamSelections: PreSubmitTeamSelectionDto[] = [];

        for (const pid of this.familyPlayers.selectedPlayerIds()) {
            const teamIds = this.eligibility.selectedTeams()[pid] ?? [];
            if (teamIds.length === 0) continue;
            const formValues = this.playerForms.buildPreSubmitFormValuesForPlayer(
                pid, schemas, waiverFieldNames, waiversGateOk,
                key => this.jobCtx.isWaiverAccepted(key),
                p => this.eligibility.getEligibilityForPlayer(p),
                () => this.eligibility.determineEligibilityField(schemas),
            );
            this.injectRequiredTeamField(formValues, teamIds, schemas);
            for (const tid of teamIds) teamSelections.push({ playerId: pid, teamId: tid, formValues });
        }
        return { jobPath, teamSelections };
    }

    private injectRequiredTeamField(
        formValues: { [key: string]: Json },
        teamIds: string[],
        schemas: import('../types/player-wizard.types').PlayerProfileFieldSchema[],
    ): void {
        const schema = schemas.find(s => s.name.toLowerCase() === 'teamid');
        if (!schema?.required) return;
        const existing = Object.entries(formValues).find(([k]) => k.toLowerCase() === 'teamid');
        if (existing && typeof existing[1] === 'string' && existing[1].trim()) return;
        // Only a single-team (PP) selection populates the required teamId field; CAC's
        // multi-select carries its teams via the per-selection loop, not this scalar field.
        if (teamIds.length === 1) formValues['teamId'] = teamIds[0];
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
                error: () => {
                    // Interceptor safety net handles the toast.
                    this._confirmation.set(null);
                },
            });
    }

    async resendConfirmationEmail(): Promise<boolean> {
        const apiBase = this.jobCtx.resolveApiBase();
        try {
            await firstValueFrom(this.http.post(`${apiBase}/player-registration/confirmation/resend`, null, { context: skipErrorToast() }));
            return true;
        } catch {
            // Component handles UX via the boolean return value.
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
        this.insuranceState.reset();
        this.insuranceSvc.reset();
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
