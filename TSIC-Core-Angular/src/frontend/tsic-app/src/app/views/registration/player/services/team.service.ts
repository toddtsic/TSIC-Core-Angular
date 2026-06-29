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

@Injectable({ providedIn: 'root' })
export class TeamService {
    private readonly http = inject(HttpClient);
    private readonly jobCtx = inject(JobContextService);
    private readonly eligibility = inject(EligibilityService);

    // raw teams for current job
    private readonly _teams = signal<AvailableTeam[] | null>(null);
    readonly allTeams = this._teams.asReadonly();
    // distinct club names present at the job — for the BYCLUBNAME "Choose Player Club"
    // picker. Sourced from /clubs (window-independent), NOT derived from allTeams, whose
    // registration-window filter would drop clubs whose teams are out of window.
    private readonly _clubs = signal<string[]>([]);
    readonly clubNames = this._clubs.asReadonly();
    // loading + error state signals
    private readonly _loading = signal<boolean>(false);
    private readonly _error = signal<string | null>(null);
    readonly loading = this._loading.asReadonly();
    readonly error = this._error.asReadonly();

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
            case 'BYAGEGROUP': {
                // Exact agegroup match. A "WAITLIST - X" pick (a full agegroup) points straight at
                // the real twin agegroup, whose teams now exist in the dataset (minted with the
                // source team's availability window). No prefix stripping — the value resolves to
                // the twin directly, so a full agegroup shows ONLY its $0 waitlist team.
                const needle = v.toLowerCase();
                return teams.filter(t => (t.agegroupName || '').toLowerCase() === needle);
            }
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
     * Agegroup-level availability for the eligibility picker — the same "all dataset vs available
     * view" split we use at the team level, applied to age groups. The FULL agegroup set still
     * resolves/labels a prior registration's agegroup (a locked active player keeps showing their
     * real agegroup even when its only team is full); this builds the *pickable* view for a NEW
     * selection:
     *   - a real agegroup with ≥1 open seat  → offered under its real name
     *   - a real agegroup whose teams are ALL full → offered as its `WAITLIST - {name}` twin.
     *
     * The twin agegroup/team is only minted at PreSubmit (EnsureWaitlistMirrorsForFilledTeams), so
     * at selection time it is NOT in the dataset yet — we SYNTHESIZE the name using the exact same
     * `WAITLIST - {name}` format the mint uses. Picking it resolves (via filterByEligibility) back
     * to this agegroup's full teams, surfaced as $0 waitlist; PreSubmit then mints the real twin
     * and reconciles the seat. "Open seat" here is genuine (a team with rosterIsFull=false) —
     * distinct from team-level isAvailable(), which treats a full team as still-pickable as $0.
     */
    availableAgegroupOptions(): string[] {
        const teams = this._teams();
        if (!teams) return [];
        const self = teams.filter(t => t.agegroupAllowsSelfRostering === true && !!t.agegroupName);
        const isWl = (n: string) => n.toUpperCase().startsWith('WAITLIST -');
        const hasOpenSeat = (t: AvailableTeam) => !t.rosterIsFull && t.feeConfigured !== false;

        // Real (non-twin) agegroup names; a twin already in the data (post-PreSubmit) collapses
        // onto the synthesized name below via the Set, so it never double-lists.
        const realNames = [...new Set(
            self.filter(t => !isWl(t.agegroupName!.trim())).map(t => t.agegroupName!.trim())
        )];

        const out = new Set<string>();
        for (const name of realNames) {
            const anyOpen = self.some(t => t.agegroupName!.trim() === name && hasOpenSeat(t));
            out.add(anyOpen ? name : `WAITLIST - ${name}`);
        }
        return [...out];
    }

    /**
     * Replace the team dataset with a fresh server snapshot. The PreSubmit round-trip
     * returns raw teams reflecting post-reconcile occupancy; pushing them here re-derives every
     * dependent signal (filteredTeams, grouped) and resolver (getTeamById) in one shot, so the
     * wizard reflects current truth instead of stale init-time data. No-op on null/undefined
     * (the server signals "no change" that way).
     */
    applyRawTeams(teams: AvailableTeam[] | null | undefined): void {
        if (!teams) return;
        this._teams.set(teams);
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
     * onLoaded fires once the teams signal is populated (fetch success or fetch error)
     * — lets callers run logic that needs the teams list without a reactive effect.
     */
    loadForJob(jobPath: string, onLoaded?: () => void): void {
        if (jobPath) {
            this.fetchClubs(jobPath);
            this.fetch(jobPath, false, onLoaded);
        } else {
            this._teams.set(null);
        }
    }

    refresh(): void {
        const jobPath = this.jobCtx.jobPath();
        if (jobPath) {
            this.fetchClubs(jobPath);
            this.fetch(jobPath, true);
        }
    }

    private fetchClubs(jobPath: string): void {
        if (!jobPath) return;
        const base = environment.apiUrl;
        this.http.get<string[]>(`${base}/jobs/${encodeURIComponent(jobPath)}/clubs`)
            .subscribe({
                next: clubs => this._clubs.set(clubs || []),
                error: err => console.error('[TeamService] failed to load clubs', err)
            });
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
