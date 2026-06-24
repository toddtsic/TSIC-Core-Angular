import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import { ToastService } from '@shared-ui/toast.service';
import { InfoTooltipComponent } from '@shared-ui/components/info-tooltip.component';
import {
    RosterSwapperService,
    UnassignedAdultQueueRowDto
} from '../services/roster-swapper.service';
import { PriorStaffAssignmentDto } from '@core/api';

/** Coach worklist status — derived per coach from requests (★self) vs live grants. */
type CoachStatus = 'unassigned' | 'partial' | 'assigned';

/** One team row in a coach's record: a request and/or grant, with its live grant state. */
interface DisplayTeam {
    teamId: string;
    displayText: string;
    /** 'self' = coach asked for it; 'admin' = director added it. */
    source: string;
    granted: boolean;
    /** Live Staff row id when granted (needed to remove the grant). */
    staffRegistrationId?: string | null;
}

/** Grid view-model row — flattens the queue DTO + the derived status/teams for the grid. */
interface QueueRow {
    raw: UnassignedAdultQueueRowDto;
    registrationId: string;
    playerName: string;
    clubName: string | null;
    contact: string;
    registrationTs: string;
    note: string | null;
    priorStaffCount: number;
    priorStaffText: string;
    /** Prior staff assignments (team · job) — shown in the "Coached before" popover. */
    priorStaff: PriorStaffAssignmentDto[];
    linkedPlayerNames: string[];
    status: CoachStatus;
    /** Semantic sort rank for status: Unassigned(0) → Partial(1) → Assigned(2). */
    statusOrder: number;
    teams: DisplayTeam[];
    grantedCount: number;
    totalCount: number;
    /** First team's label (lowercased) — the sort key for the Teams column. */
    teamSortKey: string;
    /** Requested (★self) teams not yet granted — what "Grant All" / "Grant Subset" act on. */
    ungrantedRequestIds: string[];
}

/**
 * Director approval worklist for coach (UnassignedAdult) registrations.
 *
 * One alpha-sorted SF Grid row per coach. Status (Unassigned / Partial / Assigned) is a
 * VISUAL layer over a STABLE alpha sort, so acting on a coach recolors the row in place —
 * it never moves or vanishes under the cursor. Status filter chips carry live counts (the
 * progress meter) and are NON-DESTRUCTIVE: picking a chip pins the visible set; granting a
 * coach updates its status + the counts but leaves the row pinned until you re-pick a chip
 * or refresh. Per coach: "Grant All" honors every requested team in one click; "Grant
 * Subset" (multi-team only) opens a popup to grant a chosen subset. Deny removes the coach.
 */
@Component({
    selector: 'app-coach-approval-queue',
    standalone: true,
    imports: [CommonModule, GridAllModule, InfoTooltipComponent],
    schemas: [CUSTOM_ELEMENTS_SCHEMA],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './coach-approval-queue.component.html',
    styleUrl: './coach-approval-queue.component.scss'
})
export class CoachApprovalQueueComponent implements OnInit {
    private readonly swapperService = inject(RosterSwapperService);
    private readonly toast = inject(ToastService);

    readonly queue = signal<UnassignedAdultQueueRowDto[]>([]);
    readonly isLoading = signal(false);
    /** registrationId of a coach whose grants are being mutated (drives row spinner). */
    readonly busyCoach = signal<string | null>(null);
    /** registrationId armed for a Deny confirm; null when idle. */
    readonly confirmingDeny = signal<string | null>(null);

    /** Active status lens. */
    readonly activeFilter = signal<CoachStatus | 'all'>('all');
    readonly searchTerm = signal('');
    /** Active sort column field, or null for the default stable alpha-by-name order. */
    readonly sortField = signal<string | null>(null);
    readonly sortDir = signal<'asc' | 'desc'>('asc');
    /**
     * Non-destructive filter: the set of rows currently shown. Snapshotted when a chip or
     * search is applied, NOT recomputed on every grant — so a coach you just acted on stays
     * visible (recolored) instead of being ejected from a filtered view.
     */
    private readonly pinnedIds = signal<ReadonlySet<string>>(new Set());

    // ── Grant Subset popup ──
    readonly subsetRow = signal<QueueRow | null>(null);
    readonly subsetChecked = signal<ReadonlySet<string>>(new Set());
    readonly subsetTop = signal(0);
    readonly subsetLeft = signal(0);

    // ── Multi-team read-only popup (the "N teams ▾" dropdown) ──
    readonly teamsRow = signal<QueueRow | null>(null);
    readonly teamsTop = signal(0);
    readonly teamsLeft = signal(0);

    // ── "Coached before" prior-assignments popup ──
    readonly priorRow = signal<QueueRow | null>(null);
    readonly priorTop = signal(0);
    readonly priorLeft = signal(0);
    /** Pinned = opened by click/tap: stays open (with backdrop) until dismissed; hover ignored. */
    readonly priorPinned = signal(false);

    /** Derived view-model rows, alpha-sorted by coach name (the stable key). */
    readonly rows = computed<QueueRow[]>(() =>
        this.queue()
            .map(r => this.toRow(r))
            .sort((a, b) => a.playerName.localeCompare(b.playerName))
    );

    readonly hasRows = computed(() => this.rows().length > 0);

    /** Live per-status counts for the chips (the drain meter). */
    readonly counts = computed(() => {
        const c = { all: 0, unassigned: 0, partial: 0, assigned: 0 };
        for (const r of this.rows()) {
            c.all++;
            c[r.status]++;
        }
        return c;
    });

    /**
     * Rows shown in the grid = pinned set ∩ current rows, then sorted. Default (no active
     * sort) keeps the stable alpha-by-name order from rows(); a clicked header sorts by that
     * column (status semantically, teams by label). Sorting composes with the pin filter.
     */
    readonly gridData = computed<QueueRow[]>(() => {
        const pin = this.pinnedIds();
        const visible = this.rows().filter(r => pin.has(r.registrationId));
        const field = this.sortField();
        if (!field) return visible;
        const dir = this.sortDir() === 'asc' ? 1 : -1;
        return [...visible].sort((a, b) => {
            if (field === 'status') return (a.statusOrder - b.statusOrder) * dir;
            const va = field === 'teamSortKey' ? a.teamSortKey : a.playerName;
            const vb = field === 'teamSortKey' ? b.teamSortKey : b.playerName;
            return va.localeCompare(vb) * dir;
        });
    });

    ngOnInit(): void {
        this.load();
    }

    load(silent = false): void {
        if (!silent) this.isLoading.set(true);
        this.swapperService.getUnassignedQueue().subscribe({
            next: rows => {
                this.queue.set(rows);
                this.isLoading.set(false);
                // Re-pin only on an EXPLICIT load (initial / Refresh). A silent re-fetch
                // (after a grant) must NOT re-snapshot — re-pinning would eject the coach
                // you just acted on from a status-filtered view. The acted-on row stays
                // pinned (recolored in place); its id is still present in rows().
                if (!silent) this.applyFilter(this.activeFilter(), this.searchTerm());
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load the coach approval queue.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    // ── Filtering (explicit, non-destructive) ──

    selectFilter(f: CoachStatus | 'all'): void {
        this.activeFilter.set(f);
        this.applyFilter(f, this.searchTerm());
    }

    onSearch(term: string): void {
        this.searchTerm.set(term);
        this.applyFilter(this.activeFilter(), term);
    }

    /** Snapshot the visible set from the CURRENT rows for the given lens + search. */
    private applyFilter(f: CoachStatus | 'all', term: string): void {
        const q = term.trim().toLowerCase();
        const ids = new Set<string>();
        for (const r of this.rows()) {
            if (f !== 'all' && r.status !== f) continue;
            if (q && !this.matchesSearch(r, q)) continue;
            ids.add(r.registrationId);
        }
        this.pinnedIds.set(ids);
    }

    private matchesSearch(r: QueueRow, q: string): boolean {
        return r.playerName.toLowerCase().includes(q)
            || (r.clubName?.toLowerCase().includes(q) ?? false)
            || r.contact.toLowerCase().includes(q);
    }

    // ── Row → view-model ──

    private toRow(r: UnassignedAdultQueueRowDto): QueueRow {
        const grantedIds = new Set(r.assignedTeams.map(t => t.teamId));
        const staffByTeam = new Map(r.assignedTeams.map(t => [t.teamId, t.staffRegistrationId]));
        const teams: DisplayTeam[] = r.recordedTeams.map(t => ({
            teamId: t.teamId,
            displayText: t.displayText,
            source: t.source,
            granted: grantedIds.has(t.teamId),
            staffRegistrationId: staffByTeam.get(t.teamId),
        }));

        const selfAsks = teams.filter(t => t.source === 'self');
        const grantedOfRequested = selfAsks.filter(t => t.granted).length;
        let status: CoachStatus;
        if (selfAsks.length > 0) {
            status = grantedOfRequested === 0 ? 'unassigned'
                : grantedOfRequested < selfAsks.length ? 'partial'
                    : 'assigned';
        } else {
            status = r.assignedTeams.length > 0 ? 'assigned' : 'unassigned';
        }

        return {
            raw: r,
            registrationId: r.registrationId,
            playerName: r.playerName,
            clubName: r.clubName ?? null,
            contact: this.contactLine(r),
            registrationTs: r.registrationTs,
            note: r.note ?? null,
            priorStaffCount: r.priorStaff.length,
            priorStaffText: r.priorStaff.map(p => `${p.teamName} · ${p.jobName}`).join('\n'),
            priorStaff: [...r.priorStaff].sort((a, b) =>
                a.jobName.localeCompare(b.jobName) || a.teamName.localeCompare(b.teamName)),
            linkedPlayerNames: r.linkedPlayerNames,
            status,
            statusOrder: status === 'unassigned' ? 0 : status === 'partial' ? 1 : 2,
            teams,
            grantedCount: teams.filter(t => t.granted).length,
            totalCount: teams.length,
            teamSortKey: (teams[0]?.displayText ?? '').toLowerCase(),
            ungrantedRequestIds: selfAsks.filter(t => !t.granted).map(t => t.teamId),
        };
    }

    private contactLine(row: UnassignedAdultQueueRowDto): string {
        const cityState = [row.city, row.state].filter(Boolean).join(', ');
        return [row.email, row.cellphone, cityState].filter(Boolean).join(' · ');
    }

    statusLabel(s: CoachStatus): string {
        return s === 'unassigned' ? 'Unassigned' : s === 'partial' ? 'Partial' : 'Assigned';
    }

    /**
     * Sort-arrow glyph for a sortable header. We cancel SF's native sort (so it never paints
     * its own indicator), so the header templates render this instead: a faint up/down idle
     * hint on inactive columns, a solid caret showing the active direction on the sorted one.
     */
    sortIcon(field: string): string {
        if (this.sortField() !== field) return 'bi-arrow-down-up sort-idle';
        return this.sortDir() === 'asc' ? 'bi-caret-up-fill sort-active' : 'bi-caret-down-fill sort-active';
    }

    /** Paint a status class on the row so the left-border color tracks status in place. */
    onRowDataBound(args: { data?: QueueRow; row?: HTMLElement }): void {
        const row = args.data;
        if (!row || !args.row) return;
        args.row.classList.add(`status-${row.status}`);
    }

    /** Intercept SF's sort: cancel its in-place sort and drive our own signal instead, so
     *  sorting composes with the pin filter (which lives in the gridData computed). */
    onActionBegin(args: { requestType?: string; columnName?: string; direction?: string; cancel?: boolean }): void {
        if (args.requestType === 'sorting') {
            args.cancel = true;
            if (args.columnName) {
                // We cancel SF's sort, so it never tracks direction — it reports "Ascending"
                // on every click. Own the toggle: same column flips dir, a new column starts asc.
                if (this.sortField() === args.columnName) {
                    this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
                } else {
                    this.sortField.set(args.columnName);
                    this.sortDir.set('asc');
                }
            }
        }
    }

    /** Stamp 1-based row numbers in the unbound `#` column (re-runs on every rebind). */
    refreshRowNumbers(grid: GridComponent): void {
        grid.getRows().forEach((row, i) => {
            const cell = row.querySelector('td.row-number-cell');
            if (cell) cell.textContent = String(i + 1);
        });
    }

    // ── Multi-team read-only popup ──

    openTeams(event: MouseEvent, row: QueueRow): void {
        event.stopPropagation();
        const btn = event.currentTarget as HTMLElement;
        const rect = btn.getBoundingClientRect();
        this.teamsTop.set(rect.bottom + 4);
        this.teamsLeft.set(Math.max(8, rect.left));
        this.teamsRow.set(row);
    }

    closeTeams(): void {
        this.teamsRow.set(null);
    }

    // ── "Coached before" prior-assignments popup (hover preview + click/tap pin) ──

    /** Hover-open a preview (does nothing if a pinned popup is already showing). */
    hoverPrior(event: MouseEvent, row: QueueRow): void {
        if (this.priorPinned()) return;
        this.positionPrior(event);
        this.priorRow.set(row);
    }

    /** Mouse left the trigger — close the hover preview, but leave a pinned popup open. */
    leavePrior(): void {
        if (!this.priorPinned()) this.priorRow.set(null);
    }

    /** Click/tap pins the popup open (backdrop appears); a second click toggles it closed. */
    pinPrior(event: MouseEvent, row: QueueRow): void {
        event.stopPropagation();
        if (this.priorPinned() && this.priorRow() === row) {
            this.closePrior();
            return;
        }
        this.positionPrior(event);
        this.priorRow.set(row);
        this.priorPinned.set(true);
    }

    closePrior(): void {
        this.priorRow.set(null);
        this.priorPinned.set(false);
    }

    private positionPrior(event: MouseEvent): void {
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        this.priorTop.set(rect.bottom + 4);
        this.priorLeft.set(Math.max(8, rect.left));
    }

    // ── Grant All ──

    /** Grant every requested team the coach hasn't been granted yet. */
    grantAll(row: QueueRow): void {
        const ids = row.ungrantedRequestIds;
        if (ids.length === 0 || this.busyCoach()) return;
        this.busyCoach.set(row.registrationId);
        forkJoin(ids.map(id => this.swapperService.approveRequest(row.registrationId, id))).subscribe({
            next: () => {
                this.toast.show(`${row.playerName} granted ${ids.length} team(s).`, 'success', 3000);
                this.busyCoach.set(null);
                this.load(true);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not grant — refreshing.', 'danger', 4000);
                this.busyCoach.set(null);
                this.load(true);
            }
        });
    }

    /** True when the row has at least one ungranted request to act on. */
    canGrantAll(row: QueueRow): boolean {
        return row.ungrantedRequestIds.length > 0;
    }

    /** Grant Subset only makes sense for multi-team requesters (subset of 1 = all). */
    canGrantSubset(row: QueueRow): boolean {
        return row.teams.filter(t => t.source === 'self').length > 1;
    }

    // ── Grant Subset popup ──

    openSubset(event: MouseEvent, row: QueueRow): void {
        event.stopPropagation();
        const btn = event.currentTarget as HTMLElement;
        const rect = btn.getBoundingClientRect();
        this.subsetTop.set(rect.bottom + 4);
        this.subsetLeft.set(Math.max(8, rect.right - 300));
        // Seed checks with the teams currently granted.
        this.subsetChecked.set(new Set(row.teams.filter(t => t.granted).map(t => t.teamId)));
        this.subsetRow.set(row);
    }

    closeSubset(): void {
        this.subsetRow.set(null);
    }

    toggleSubset(teamId: string): void {
        const next = new Set(this.subsetChecked());
        next.has(teamId) ? next.delete(teamId) : next.add(teamId);
        this.subsetChecked.set(next);
    }

    isSubsetChecked(teamId: string): boolean {
        return this.subsetChecked().has(teamId);
    }

    /** Apply the subset: grant newly-checked teams, remove newly-unchecked ones. */
    applySubset(): void {
        const row = this.subsetRow();
        if (!row) return;
        const checked = this.subsetChecked();
        const current = new Set(row.teams.filter(t => t.granted).map(t => t.teamId));

        const added = [...checked].filter(id => !current.has(id));
        const removed = [...current].filter(id => !checked.has(id));
        this.closeSubset();
        if (added.length === 0 && removed.length === 0) return;

        const ops = [
            ...added.map(id => this.swapperService.approveRequest(row.registrationId, id)),
            ...removed.map(id => {
                const staffRegId = row.teams.find(t => t.teamId === id)?.staffRegistrationId;
                return this.swapperService.removeStaffFromTeam(staffRegId!, id);
            }),
        ];

        this.busyCoach.set(row.registrationId);
        forkJoin(ops).subscribe({
            next: () => {
                const parts = [
                    added.length ? `granted ${added.length}` : '',
                    removed.length ? `removed ${removed.length}` : '',
                ].filter(Boolean).join(', ');
                this.toast.show(`${row.playerName} — ${parts}.`, 'success', 3000);
                this.busyCoach.set(null);
                this.load(true);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not update — refreshing.', 'danger', 4000);
                this.busyCoach.set(null);
                this.load(true);
            }
        });
    }

    // ── Deny ──

    requestDeny(row: QueueRow): void {
        this.confirmingDeny.set(row.registrationId);
    }

    cancelDeny(): void {
        this.confirmingDeny.set(null);
    }

    confirmDeny(row: QueueRow): void {
        if (this.busyCoach() === row.registrationId) return;
        this.confirmingDeny.set(null);
        this.busyCoach.set(row.registrationId);
        this.swapperService.denyCoach(row.registrationId).subscribe({
            next: () => {
                // Denied coach drops out of queue() → naturally gone from gridData (it filters
                // rows by pinnedIds); no re-pin needed, so other rows stay put.
                this.queue.set(this.queue().filter(r => r.registrationId !== row.registrationId));
                this.toast.show(`${row.playerName} denied — removed from all teams and deactivated.`, 'info', 3500);
                this.busyCoach.set(null);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not deny this coach.', 'danger', 4000);
                this.busyCoach.set(null);
            }
        });
    }
}
