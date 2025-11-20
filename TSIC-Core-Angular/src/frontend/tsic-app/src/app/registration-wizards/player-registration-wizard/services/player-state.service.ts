import { Injectable, inject, signal, effect } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';

/**
 * PlayerStateService: incremental extraction of player selection + team mapping + eligibility.
 * Phase 1: proxies/synchronizes with RegistrationWizardService signals (non-breaking).
 * Future phases will migrate ownership fully and remove sync effect & wizard fields.
 */
@Injectable({ providedIn: 'root' })
export class PlayerStateService {
  private readonly wizard = inject(RegistrationWizardService);

  // Local signals (start with wizard values; keep synchronized)
  private readonly _selectedTeams = signal<Record<string, string | string[]>>({});
  private readonly _eligibilityByPlayer = signal<Record<string, string>>({});

  constructor() {
    try {
      // Seed initial values synchronously to avoid transient empty state in computed consumers
      this._selectedTeams.set({ ...this.wizard.selectedTeams() });
      this._eligibilityByPlayer.set({ ...this.wizard.eligibilityByPlayer() });
    } catch { /* ignore */ }
  }

  // Public accessors
  selectedTeams(): Record<string, string | string[]> { return this._selectedTeams(); }
  eligibilityByPlayer(): Record<string, string> { return this._eligibilityByPlayer(); }

  // Synchronization effect: keep local copies in sync with wizard until full migration
  private readonly _sync = effect(() => {
    try {
      const st = this.wizard.selectedTeams();
      const el = this.wizard.eligibilityByPlayer();
      const curSt = this._selectedTeams();
      const curEl = this._eligibilityByPlayer();
      // Shallow comparison to avoid redundant sets
      const changedTeams = Object.keys(st).length !== Object.keys(curSt).length || Object.keys(st).some(k => curSt[k] !== st[k]);
      const changedElig = Object.keys(el).length !== Object.keys(curEl).length || Object.keys(el).some(k => curEl[k] !== el[k]);
      if (changedTeams) this._selectedTeams.set({ ...st });
      if (changedElig) this._eligibilityByPlayer.set({ ...el });
    } catch { /* ignore */ }
  });

  // Mutators (write-through to wizard for now)
  setSelectedTeams(map: Record<string, string | string[]>): void {
    this.wizard.selectedTeams.set({ ...map }); // authoritative write
    this._selectedTeams.set({ ...map });
  }
  setEligibilityForPlayer(playerId: string, value: string | null | undefined): void {
    this.wizard.setEligibilityForPlayer(playerId, value);
    const m = { ...this._eligibilityByPlayer() };
    if (value == null || value === '') delete m[playerId]; else m[playerId] = String(value);
    this._eligibilityByPlayer.set(m);
  }
  getEligibilityForPlayer(playerId: string): string | undefined {
    return this._eligibilityByPlayer()[playerId];
  }

  // Convenience wrappers
  familyPlayers() { return this.wizard.familyPlayers(); }
  selectedPlayerIds(): string[] { return this.wizard.familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId); }
}
