import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
// Import the default environment, but we'll dynamically prefer the local dev API when running on localhost.
import { environment } from '../../../environments/environment';

export type PaymentOption = 'PIF' | 'Deposit';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');

    // Start mode selection: 'new' (start fresh), 'edit' (edit prior), 'parent' (update/deassign)
    startMode = signal<'new' | 'edit' | 'parent' | null>(null);

    // Family account presence (from Family Check step)
    hasFamilyAccount = signal<'yes' | 'no' | null>(null);

    // Players and selections
    selectedPlayers = signal<Array<{ userId: string; name: string }>>([]);
    // Family players available for registration
    familyPlayers = signal<Array<{ playerId: string; firstName: string; lastName: string; gender: string; dob?: string; registered?: boolean }>>([]);
    // Include username explicitly for UI badge (displayName kept for future flexibility)
    activeFamilyUser = signal<{ familyUserId: string; displayName: string; userName: string } | null>(null);
    familyUsers = signal<Array<{ familyUserId: string; displayName: string; userName: string }>>([]);
    // Whether an existing player registration for the current job + active family user already exists.
    // null = unknown/not yet checked; true/false = definitive.
    existingRegistrationAvailable = signal<boolean | null>(null);
    // Eligibility type is job-wide (e.g., BYGRADYEAR), but the selected value is PER PLAYER
    teamConstraintType = signal<string | null>(null); // e.g., BYGRADYEAR
    // Deprecated: legacy single eligibility value (kept for backward compatibility where needed)
    teamConstraintValue = signal<string | null>(null); // e.g., 2027
    // New: per-player eligibility selection map (playerId -> value)
    eligibilityByPlayer = signal<Record<string, string>>({});
    selectedTeams = signal<Record<string, string>>({}); // playerId -> teamId

    // Forms data per player (dynamic fields later)
    formData = signal<Record<string, any>>({}); // playerId -> { fieldName: value }

    // Payment
    paymentOption = signal<PaymentOption>('PIF');

    reset(): void {
        this.startMode.set(null);
        this.hasFamilyAccount.set(null);
        this.selectedPlayers.set([]);
        this.familyPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.eligibilityByPlayer.set({});
        this.selectedTeams.set({});
        this.formData.set({});
        this.paymentOption.set('PIF');
        this.activeFamilyUser.set(null);
        this.familyUsers.set([]);
        this.existingRegistrationAvailable.set(null);
    }

    loadFamilyUsers(jobPath: string): void {
        if (!jobPath) return;
        // Future: cache by jobPath; for now simple fetch
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family users', { jobPath, base });
        this.http.get<Array<{ familyUserId: string; displayName: string; userName: string }>>(`${base}/family/users`, { params: { jobPath } })
            .subscribe({
                next: users => {
                    this.familyUsers.set(users || []);
                    // Auto-select if exactly one user
                    if (users?.length === 1) {
                        this.activeFamilyUser.set(users[0]);
                    }
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family users', err);
                    this.familyUsers.set([]);
                }
            });
    }

    loadFamilyPlayers(jobPath: string, familyUserId: string): void {
        if (!jobPath || !familyUserId) return;
        const base = this.resolveApiBase();
        console.log('[RegWizard] GET family players', { jobPath, familyUserId, base });
        this.http.get<Array<{ playerId: string; firstName: string; lastName: string; gender: string; dob?: string; registered?: boolean }>>(`${base}/family/players`, { params: { jobPath, familyUserId, debug: '1' } })
            .subscribe({
                next: players => {
                    const list = players || [];
                    this.familyPlayers.set(list);
                    // Pre-select already registered players and lock them via selectedPlayers list
                    const preselected = list.filter(p => p.registered).map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
                    this.selectedPlayers.set(preselected);
                    console.log('[RegWizard] Loaded players', { count: list.length, preselected });
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family players', err);
                    this.familyPlayers.set([]);
                }
            });
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

    togglePlayerSelection(player: { playerId: string; firstName: string; lastName: string; registered?: boolean }): void {
        if (player.registered) return; // cannot deselect previously registered players
        const current = this.selectedPlayers();
        const id = player.playerId;
        const exists = current.some(p => p.userId === id);
        if (exists) {
            this.selectedPlayers.set(current.filter(p => p.userId !== id));
            // drop eligibility and team assignment for deselected player
            const elig = { ...this.eligibilityByPlayer() };
            delete elig[id];
            this.eligibilityByPlayer.set(elig);
            const teams = { ...this.selectedTeams() };
            delete teams[id];
            this.selectedTeams.set(teams);
        } else {
            this.selectedPlayers.set([...current, { userId: id, name: `${player.firstName} ${player.lastName}`.trim() }]);
        }
    }

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
}
