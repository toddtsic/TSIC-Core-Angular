import { Injectable, signal } from '@angular/core';

/**
 * PlayerStateService: incremental extraction of player selection + team mapping + eligibility.
 * Phase 2: authoritative ownership of player team selections and eligibility.
 * RegistrationWizardService now delegates all reads/writes to this service.
 */
@Injectable({ providedIn: 'root' })
export class PlayerStateService {
    // Authoritative signals
    private readonly _selectedTeams = signal<Record<string, string | string[]>>({});
    private readonly _eligibilityByPlayer = signal<Record<string, string>>({});

    constructor() { /* no-op */ }

    // Public accessors
    selectedTeams(): Record<string, string | string[]> { return this._selectedTeams(); }
    eligibilityByPlayer(): Record<string, string> { return this._eligibilityByPlayer(); }

    // Mutators
    setSelectedTeams(map: Record<string, string | string[]>): void {
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
}
