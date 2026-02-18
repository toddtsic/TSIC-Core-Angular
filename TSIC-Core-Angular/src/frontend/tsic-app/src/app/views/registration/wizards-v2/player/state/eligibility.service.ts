import { Injectable, inject, signal } from '@angular/core';
import { PlayerStateService } from '@views/registration/wizards/player-registration-wizard/services/player-state.service';
import { hasAllParts } from '@views/registration/wizards/shared/utils/property-utils';
import type { PlayerProfileFieldSchema, PlayerFormFieldValue, FamilyPlayerDto } from '../types/player-wizard.types';

/**
 * Eligibility Service — owns constraint type/value and delegates per-player
 * eligibility + team selections to PlayerStateService.
 *
 * Extracted from RegistrationWizardService lines ~88-99, 672-727, 1096-1120.
 * Gold-standard signal pattern throughout.
 */
@Injectable({ providedIn: 'root' })
export class EligibilityService {
    private readonly playerState = inject(PlayerStateService);

    // ── Constraint type/value ─────────────────────────────────────────
    private readonly _teamConstraintType = signal<string | null>(null);
    private readonly _teamConstraintValue = signal<string | null>(null);
    readonly teamConstraintType = this._teamConstraintType.asReadonly();
    readonly teamConstraintValue = this._teamConstraintValue.asReadonly();

    // ── Controlled mutators ───────────────────────────────────────────
    setTeamConstraintType(v: string | null): void { this._teamConstraintType.set(v); }
    setTeamConstraintValue(v: string | null): void { this._teamConstraintValue.set(v); }

    // ── Team selection facades ────────────────────────────────────────
    selectedTeams(): Record<string, string | string[]> { return this.playerState.selectedTeams(); }
    setSelectedTeams(map: Record<string, string | string[]>): void { this.playerState.setSelectedTeams(map); }

    // ── Per-player eligibility facades ────────────────────────────────
    eligibilityByPlayer(): Record<string, string> { return this.playerState.eligibilityByPlayer(); }
    setEligibilityForPlayer(playerId: string, value: string | null | undefined): void { this.playerState.setEligibilityForPlayer(playerId, value); }
    getEligibilityForPlayer(playerId: string): string | undefined { return this.playerState.getEligibilityForPlayer(playerId); }

    // ── Seed eligibility from schemas after metadata load ─────────────
    /**
     * After profile metadata is parsed, scan form values for the eligibility
     * field and seed per-player eligibility if not already set.
     */
    seedEligibilityFromSchemas(
        schemas: PlayerProfileFieldSchema[],
        players: FamilyPlayerDto[],
        selectedPlayerIds: string[],
        getFormValue: (playerId: string, fieldName: string) => unknown,
    ): void {
        try {
            const eligField = this.determineEligibilityField(schemas);
            if (!eligField) return;
            const map = { ...this.playerState.eligibilityByPlayer() } as Record<string, string>;
            for (const p of players) {
                if (!p.registered && !p.selected) continue;
                const v = getFormValue(p.playerId, eligField);
                if (v != null && String(v).trim() !== '') {
                    const existing = map[p.playerId];
                    if (!existing || String(existing).trim() === '') {
                        map[p.playerId] = String(v).trim();
                    }
                }
            }
            if (!Object.keys(map).length) return;
            for (const [pid, val] of Object.entries(map)) {
                this.playerState.setEligibilityForPlayer(pid, val);
            }
            this.updateUnifiedConstraintValue(selectedPlayerIds, map);
        } catch { /* ignore */ }
    }

    /** Seed eligibility for a single newly-selected player from their defaults. */
    applyEligibilityFromDefaults(
        playerId: string,
        schemas: PlayerProfileFieldSchema[],
        getFormValue: (pid: string, field: string) => unknown,
        selectedPlayerIds: string[],
    ): void {
        try {
            const eligField = this.determineEligibilityField(schemas);
            if (!eligField) return;
            const rawElig = getFormValue(playerId, eligField);
            if (rawElig == null || String(rawElig).trim() === '') return;
            const existing = this.getEligibilityForPlayer(playerId);
            if (existing && String(existing).trim() !== '') return;
            this.playerState.setEligibilityForPlayer(playerId, String(rawElig).trim());
            this.updateUnifiedConstraintValue(selectedPlayerIds);
        } catch { /* ignore */ }
    }

    /** Determine the schema field name for the eligibility constraint. */
    determineEligibilityField(schemas: PlayerProfileFieldSchema[]): string | null {
        const tctype = (this._teamConstraintType() || '').toUpperCase();
        if (!tctype || !schemas.length) return null;
        const candidates = schemas.filter(f =>
            (f.visibility ?? 'public') !== 'hidden' && (f.visibility ?? 'public') !== 'adminOnly',
        );
        const byName = (parts: string[]) =>
            candidates.find(f =>
                hasAllParts(f.name.toLowerCase(), parts) || hasAllParts(f.label.toLowerCase(), parts),
            );
        if (tctype === 'BYGRADYEAR') return byName(['grad', 'year'])?.name || null;
        if (tctype === 'BYAGEGROUP') return byName(['age', 'group'])?.name || null;
        if (tctype === 'BYAGERANGE') return byName(['age', 'range'])?.name || null;
        if (tctype === 'BYCLUBNAME') return byName(['club'])?.name || null;
        return null;
    }

    /** If all selected players share the same eligibility value, set the unified constraint value. */
    updateUnifiedConstraintValue(
        selectedPlayerIds: string[],
        eligMap?: Record<string, string>,
    ): void {
        const map = eligMap ?? this.playerState.eligibilityByPlayer();
        const values = selectedPlayerIds
            .map(id => map[id])
            .filter(v => !!v);
        const unique = Array.from(new Set(values));
        if (unique.length === 1) this._teamConstraintValue.set(unique[0]);
    }

    /** Prune team selections for deselected players. */
    pruneDeselectedTeams(selectedIds: Set<string>): void {
        const teams = { ...this.playerState.selectedTeams() };
        let changed = false;
        for (const pid of Object.keys(teams)) {
            if (!selectedIds.has(pid)) {
                delete teams[pid];
                changed = true;
            }
        }
        if (changed) this.playerState.setSelectedTeams(teams);
    }

    // ── Reset ─────────────────────────────────────────────────────────
    reset(): void {
        this._teamConstraintType.set(null);
        this._teamConstraintValue.set(null);
        this.playerState.reset();
    }
}
