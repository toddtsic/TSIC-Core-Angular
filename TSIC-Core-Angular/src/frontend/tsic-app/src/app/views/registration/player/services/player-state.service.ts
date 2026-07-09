import { Injectable, signal } from '@angular/core';

/**
 * PlayerStateService: incremental extraction of player selection + team mapping + eligibility.
 * Phase 2: authoritative ownership of player team selections and eligibility.
 * RegistrationWizardService now delegates all reads/writes to this service.
 */
@Injectable({ providedIn: 'root' })
export class PlayerStateService {
    // Authoritative signals
    // A player's selection is always a list: PP mode is constrained to length <= 1,
    // CAC mode is 0..N. Never a bare string — that dual shape caused the CAC Continue-gate bug.
    private readonly _selectedTeams = signal<Record<string, string[]>>({});
    private readonly _eligibilityByPlayer = signal<Record<string, string>>({});

    constructor() { /* no-op */ }

    // Public accessors
    selectedTeams(): Record<string, string[]> { return this._selectedTeams(); }
    eligibilityByPlayer(): Record<string, string> { return this._eligibilityByPlayer(); }

    // Mutators
    setSelectedTeams(map: Record<string, string[]>): void {
        this._selectedTeams.set({ ...map });
    }
    setEligibilityForPlayer(playerId: string, value: string | null | undefined): void {
        const m = { ...this._eligibilityByPlayer() };
        if (value == null || value === '') delete m[playerId]; else m[playerId] = String(value);
        this._eligibilityByPlayer.set(m);
    }
    getEligibilityForPlayer(playerId: string): string | undefined {
        return this._eligibilityByPlayer()[playerId];
    }

    reset(): void {
        this._selectedTeams.set({});
        this._eligibilityByPlayer.set({});
    }
}
