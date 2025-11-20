import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RegistrationWizardService } from './registration-wizard.service';

// Shared model (aligns with backend AvailableTeamDto)
export interface AvailableTeam {
    teamId: string;
    teamName: string;
    agegroupId: string;
    agegroupName?: string | null;
    divisionId?: string | null;
    divisionName?: string | null;
    maxRosterSize: number;
    currentRosterSize: number;
    rosterIsFull: boolean;
    teamAllowsSelfRostering?: boolean | null;
    agegroupAllowsSelfRostering?: boolean | null;
    perRegistrantFee?: number | null;
    perRegistrantDeposit?: number | null;
    jobUsesWaitlists: boolean;
    waitlistTeamId?: string | null;
}

interface CacheEntry { data: AvailableTeam[]; ts: number; }

@Injectable({ providedIn: 'root' })
export class TeamService {
    private readonly http = inject(HttpClient);
    private readonly wizard = inject(RegistrationWizardService);

    // raw teams for current job
    private readonly _teams = signal<AvailableTeam[] | null>(null);
    // loading + error state signals
    loading = signal<boolean>(false);
    error = signal<string | null>(null);

    // simple in-memory cache keyed by jobPath
    private readonly cache = new Map<string, CacheEntry>();
    private readonly cacheTtlMs = 60_000; // 1 minute for now

    // Derived filtered collection based on eligibility constraint
    filteredTeams = computed(() => {
        const teams = this._teams();
        if (!teams) return [] as AvailableTeam[];
        const constraintType = this.wizard.teamConstraintType();
        const constraintValue = this.wizard.teamConstraintValue();
        if (!constraintType || !constraintValue) return teams;
        switch (constraintType) {
            case 'BYGRADYEAR':
                // Grad year filtering heuristics: match if name contains the year OR teamName ends with it OR agegroupName includes.
                return teams.filter(t => {
                    const yr = constraintValue;
                    const n = (t.teamName || '').toLowerCase();
                    const ag = (t.agegroupName || '').toLowerCase();
                    return n.includes(yr) || n.endsWith(yr) || ag.includes(yr);
                });
            case 'BYAGEGROUP':
                return teams.filter(t => t.agegroupName?.toLowerCase() === constraintValue.toLowerCase());
            case 'BYCLUBNAME': {
                const needle = constraintValue.toLowerCase();
                return teams.filter(t => (t.teamName || '').toLowerCase().includes(needle));
            }
            default:
                return teams;
        }
    });

    // Grouped view (Agegroup -> Division -> Teams) for UI convenience
    grouped = computed(() => {
        const list = this.filteredTeams();
        const ageMap = new Map<string, { agegroupId: string; agegroupName: string; divisions: Map<string, { divisionId: string | null; divisionName: string | null; teams: AvailableTeam[] }> }>();
        for (const t of list) {
            const agKey = t.agegroupId;
            if (!ageMap.has(agKey)) {
                ageMap.set(agKey, { agegroupId: t.agegroupId, agegroupName: t.agegroupName || 'Age Group', divisions: new Map() });
            }
            const ag = ageMap.get(agKey)!;
            const divKey = t.divisionId || 'none';
            if (!ag.divisions.has(divKey)) {
                ag.divisions.set(divKey, { divisionId: t.divisionId || null, divisionName: t.divisionName || null, teams: [] });
            }
            ag.divisions.get(divKey)!.teams.push(t);
        }
        // Normalize to arrays
        return Array.from(ageMap.values()).map(a => ({
            agegroupId: a.agegroupId,
            agegroupName: a.agegroupName,
            divisions: Array.from(a.divisions.values()).map(d => ({
                divisionId: d.divisionId,
                divisionName: d.divisionName,
                teams: [...d.teams].sort((x, y) => x.teamName.localeCompare(y.teamName))
            })).sort((x, y) => (x.divisionName || '').localeCompare(y.divisionName || ''))
        }));
    });

    // Return teams filtered by a specific eligibility value according to the current job's constraint type
    filterByEligibility(value: string | null | undefined): AvailableTeam[] {
        const teams = this._teams();
        if (!teams) return [] as AvailableTeam[];
        const constraintType = this.wizard.teamConstraintType();
        const v = (value ?? '').toString().trim();
        if (!constraintType || !v) return teams;
        switch (constraintType) {
            case 'BYGRADYEAR': {
                const neddle = v.toLowerCase();
                return teams.filter(t => {
                    const n = (t.teamName || '').toLowerCase();
                    const ag = (t.agegroupName || '').toLowerCase();
                    return n.includes(neddle) || n.endsWith(neddle) || ag.includes(neddle);
                });
            }
            case 'BYAGEGROUP':
                return teams.filter(t => (t.agegroupName || '').toLowerCase() === v.toLowerCase());
            case 'BYCLUBNAME': {
                const needle = v.toLowerCase();
                return teams.filter(t => (t.teamName || '').toLowerCase().includes(needle));
            }
            default:
                return teams;
        }
    }

    getTeamById(teamId: string): AvailableTeam | undefined {
        const teams = this._teams();
        return teams?.find(t => t.teamId === teamId);
    }

    constructor() {
        // Auto-refetch when jobPath changes. Allow signal writes inside the effect
        // because ensureLoaded/fetch update local signals like _teams/loading/error.
        effect(() => {
            const jobPath = this.wizard.jobPath();
            if (jobPath) {
                this.ensureLoaded(jobPath);
            } else {
                this._teams.set(null);
            }
        });
    }

    refresh(): void {
        const jobPath = this.wizard.jobPath();
        if (jobPath) this.fetch(jobPath, true);
    }

    private ensureLoaded(jobPath: string): void {
        const cached = this.cache.get(jobPath);
        const now = Date.now();
        if (cached && (now - cached.ts) < this.cacheTtlMs) {
            this._teams.set(cached.data);
            return;
        }
        this.fetch(jobPath, false);
    }

    private fetch(jobPath: string, force: boolean): void {
        if (!jobPath) return;
        this.loading.set(true);
        this.error.set(null);
        const base = this.resolveApiBase();
        this.http.get<AvailableTeam[]>(`${base}/jobs/${encodeURIComponent(jobPath)}/available-teams`)
            .subscribe({
                next: data => {
                    this.loading.set(false);
                    this._teams.set(data || []);
                    this.cache.set(jobPath, { data: data || [], ts: Date.now() });
                },
                error: err => {
                    console.error('[TeamService] failed to load teams', err);
                    this.loading.set(false);
                    this.error.set(err?.message || 'Failed to load teams');
                    if (!force) {
                        this._teams.set([]);
                    }
                }
            });
    }

    // Mirror logic in RegistrationWizardService for consistency
    private resolveApiBase(): string {
        try {
            const host = globalThis.location?.host?.toLowerCase?.() ?? '';
            if (host.startsWith('localhost') || host.startsWith('127.0.0.1')) {
                return 'https://localhost:7215/api';
            }
        } catch { /* SSR or no window */ }
        // Fallback: relative /api (assumes reverse proxy or same-origin deployment)
        return '/api';
    }
}
