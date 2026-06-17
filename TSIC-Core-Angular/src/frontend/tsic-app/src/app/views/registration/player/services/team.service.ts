import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { JobContextService } from '../state/job-context.service';
import { EligibilityService } from '../state/eligibility.service';
import { formatHttpError } from '../../shared/utils/error-utils';

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
    fee?: number | null;
    deposit?: number | null;
    effectiveFee?: number | null;
    /** False = no fee configured at any cascade level → not registerable; the wizard
     *  shows "Fee not set" and blocks selection instead of fabricating/charging $0. */
    feeConfigured?: boolean | null;
    /** Per-scope payment phase (server-resolved via ResolveFullPaymentPhase): true = this
     *  team must be paid in full (no deposit slice); false = a deposit may be taken. Drives
     *  per-line phase for a NEW selection that has no stamped registration yet, so a family
     *  cart spanning deposit and full-payment scopes bills each line by its own phase. */
    fullPaymentRequired?: boolean | null;
    jobUsesWaitlists: boolean;
    waitlistTeamId?: string | null;
    startDate?: string | null;
    endDate?: string | null;
    perRegistrantFee?: number | null;
    clubName?: string | null;
}

interface CacheEntry { data: AvailableTeam[]; ts: number; }

@Injectable({ providedIn: 'root' })
export class TeamService {
    private readonly http = inject(HttpClient);
    private readonly jobCtx = inject(JobContextService);
    private readonly eligibility = inject(EligibilityService);

    // raw teams for current job
    private readonly _teams = signal<AvailableTeam[] | null>(null);
    readonly allTeams = this._teams.asReadonly();
    // loading + error state signals
    private readonly _loading = signal<boolean>(false);
    private readonly _error = signal<string | null>(null);
    readonly loading = this._loading.asReadonly();
    readonly error = this._error.asReadonly();

    // simple in-memory cache keyed by jobPath
    private readonly cache = new Map<string, CacheEntry>();
    private readonly cacheTtlMs = 60_000; // 1 minute for now

    // Derived filtered collection based on eligibility constraint
    filteredTeams = computed(() => {
        const teams = this._teams();
        if (!teams) return [] as AvailableTeam[];
        const constraintType = this.eligibility.teamConstraintType();
        const constraintValue = this.eligibility.teamConstraintValue();
        if (!constraintType || !constraintValue) return teams;
        switch (constraintType) {
            case 'BYGRADYEAR':
                return teams.filter(t => {
                    const yr = constraintValue;
                    const n = (t.teamName || '').toLowerCase();
                    const ag = (t.agegroupName || '').toLowerCase();
                    return n.includes(yr) || n.endsWith(yr) || ag.includes(yr);
                });
            case 'BYAGEGROUP':
                return teams.filter(t => t.agegroupName?.toLowerCase() === constraintValue.toLowerCase());
            case 'BYAGERANGE': {
                const rangeName = constraintValue.toLowerCase();
                return teams.filter(t => (t.teamName || '').toLowerCase().includes(rangeName));
            }
            case 'BYCLUBNAME': {
                const needle = constraintValue.trim().toLowerCase();
                return teams.filter(t => (t.clubName || '').trim().toLowerCase() === needle);
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
    filterByEligibility(value: string | null | undefined, gender?: string | null): AvailableTeam[] {
        const teams = this._teams();
        if (!teams) return [] as AvailableTeam[];
        const constraintType = this.eligibility.teamConstraintType();
        const v = (value ?? '').toString().trim();
        if (!constraintType || !v) return teams;
        switch (constraintType) {
            case 'BYGRADYEAR': {
                const needle = v.toLowerCase();
                return teams.filter(t => {
                    const n = (t.teamName || '').toLowerCase();
                    const ag = (t.agegroupName || '').toLowerCase();
                    return n.includes(needle) || n.endsWith(needle) || ag.includes(needle);
                });
            }
            case 'BYAGEGROUP':
                return teams.filter(t => (t.agegroupName || '').toLowerCase() === v.toLowerCase());
            case 'BYAGERANGE': {
                const rangeName = v.toLowerCase();
                const genderWord = (gender === 'M') ? 'boys' : 'girls';
                // If no team names contain any gender word, skip gender filtering (co-ed events)
                const anyTeamHasGender = teams.some(t => {
                    const tn = (t.teamName || '').toLowerCase();
                    return tn.includes('boys') || tn.includes('girls');
                });
                return teams.filter(t => {
                    const tn = (t.teamName || '').toLowerCase();
                    return tn.includes(rangeName) && (!anyTeamHasGender || tn.includes(genderWord));
                });
            }
            case 'BYCLUBNAME': {
                const needle = v.trim().toLowerCase();
                return teams.filter(t => (t.clubName || '').trim().toLowerCase() === needle);
            }
            default:
                return teams;
        }
    }

    getTeamById(teamId: string): AvailableTeam | undefined {
        const teams = this._teams();
        return teams?.find(t => t.teamId === teamId);
    }

    /**
     * Convenience: is this team pickable for a NEW selection? Derived from the granular raw
     * flags — a full team stays "available" when the job uses waitlists (its $0 twin is offered),
     * but a team with no configured fee is not. The raw flags (rosterIsFull, feeConfigured,
     * teamAllowsSelfRostering) remain the source of truth; this is a one-call summary, not a
     * replacement for them.
     */
    isAvailable(t: AvailableTeam): boolean {
        if (t.feeConfigured === false) return false;
        if (t.rosterIsFull && !t.jobUsesWaitlists) return false;
        return true;
    }

    /**
     * Replace the team dataset with a fresh server snapshot. The PreSubmit round-trip
     * returns raw teams reflecting post-reconcile occupancy; pushing them here re-derives every
     * dependent signal (filteredTeams, grouped) and resolver (getTeamById) in one shot, so the
     * wizard reflects current truth instead of stale init-time data. Updates the cache so a later
     * read agrees. No-op on null/undefined (the server signals "no change" that way).
     */
    applyRawTeams(teams: AvailableTeam[] | null | undefined): void {
        if (!teams) return;
        this._teams.set(teams);
        const jobPath = this.jobCtx.jobPath();
        if (jobPath) this.cache.set(jobPath, { data: teams, ts: Date.now() });
    }

    /**
     * Display name for a *selected* team — always the team's real name.
     *
     * We do NOT synthesize a "WAITLIST - " prefix from rosterIsFull: a team being full
     * does not make the player viewing it waitlisted. An already-rostered, confirmed
     * member is one of the seats that filled the team — renaming her own team "WAITLIST"
     * is wrong (this was Brynn's bug). A player who is actually waitlisted sits on the
     * twin team, whose stored name already IS "WAITLIST - {name}", so it surfaces here
     * correctly without any reconstruction. A new selector picking a full team is warned
     * by the dropdown's "⚠ WAITLIST ·" option label and the waitlist-alert banner —
     * the team keeps its real name. Waitlisting is a payment-time outcome, not a rename.
     */
    getTeamDisplayName(teamId: string): string {
        const team = this.getTeamById(teamId);
        if (!team) return teamId;
        return team.teamName;
    }

    /**
     * Call explicitly when the job context is ready (replaces constructor effect).
     * onLoaded fires once the teams signal is populated (cache hit, fetch success, or fetch
     * error) — lets callers run logic that needs the teams list without a reactive effect.
     */
    loadForJob(jobPath: string, onLoaded?: () => void): void {
        if (jobPath) {
            this.ensureLoaded(jobPath, onLoaded);
        } else {
            this._teams.set(null);
        }
    }

    refresh(): void {
        const jobPath = this.jobCtx.jobPath();
        if (jobPath) this.fetch(jobPath, true);
    }

    private ensureLoaded(jobPath: string, onLoaded?: () => void): void {
        const cached = this.cache.get(jobPath);
        const now = Date.now();
        if (cached && (now - cached.ts) < this.cacheTtlMs) {
            this._teams.set(cached.data);
            onLoaded?.();
            return;
        }
        this.fetch(jobPath, false, onLoaded);
    }

    private fetch(jobPath: string, force: boolean, onLoaded?: () => void): void {
        if (!jobPath) return;
        this._loading.set(true);
        this._error.set(null);
        const base = environment.apiUrl;
        this.http.get<AvailableTeam[]>(`${base}/jobs/${encodeURIComponent(jobPath)}/available-teams`)
            .subscribe({
                next: data => {
                    this._loading.set(false);
                    this._teams.set(data || []);
                    this.cache.set(jobPath, { data: data || [], ts: Date.now() });
                    onLoaded?.();
                },
                error: err => {
                    console.error('[TeamService] failed to load teams', err);
                    this._loading.set(false);
                    this._error.set(formatHttpError(err));
                    if (!force) {
                        this._teams.set([]);
                    }
                    onLoaded?.();
                }
            });
    }
}
