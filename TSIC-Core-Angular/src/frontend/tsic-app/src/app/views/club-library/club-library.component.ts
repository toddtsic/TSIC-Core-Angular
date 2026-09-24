import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ClubTeamDto, ClubTeamEventHistoryDto, RegisteredTeamDto, TeamsMetadataResponse } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { formatLop } from '@shared/teams/lop-choices';
import { collapseHistoryPerEvent } from '@shared/teams/club-team-history';
import { clubTeamArchiveLockReason, clubTeamDeleteLockReason, clubTeamEditLockReason, clubTeamRemoval, type ClubTeamLockContext, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { TeamFormModalComponent } from '@views/registration/team/steps/team-form-modal.component';

/** One row of the library table: the library entry plus what the page knows about it. */
export interface LibraryRow {
    team: ClubTeamDto;
    /** This event's copy when registered or waitlisted here — a LOCK input (archive/delete), never a column. */
    registered: RegisteredTeamDto | null;
    /** Every event this team has been registered for, THIS event first, then newest first. */
    history: ClubTeamEventHistoryDto[];
}

/**
 * Club Team Library — the club's list of teams, and NOTHING about registering for the event
 * the rep happens to be signed in to. Todd 2026-09-24: "you are updating your list of team
 * options available when you register, you are not registering here". The wizard's fly-in is
 * the registration surface; this page is the list's home: every team, every event it has been
 * registered for (this one is just a highlighted chip), and every housekeeping action on every
 * row, in an open Actions column (the fly-in hides its kebab on registered rows; here nothing hides).
 *
 * Job-scoped by ruling (Todd, 2026-09-22): auth is job-scoped, so the page still knows this
 * event — it uses that only to lock archive/delete on a team registered here and to pin this
 * event first in Registered for. The data comes from the same metadata read the wizard
 * uses plus one history endpoint; every mutation goes through the same service calls, so the
 * two surfaces can never disagree about what a team is.
 */
@Component({
    selector: 'app-club-library',
    standalone: true,
    imports: [NgTemplateOutlet, ConfirmDialogComponent, TeamFormModalComponent],
    templateUrl: './club-library.component.html',
    styleUrl: './club-library.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubLibraryComponent implements OnInit {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly destroyRef = inject(DestroyRef);

    /** Same org-prefix strip as the wizard, so the page names the event the way the wizard does. */
    readonly eventName = computed(() => {
        const raw = this.jobService.currentJob()?.jobName ?? 'this event';
        const idx = raw.indexOf(':');
        return idx > 0 ? raw.substring(idx + 1).trim() : raw;
    });

    readonly jobPath = computed(() => this.jobService.currentJob()?.jobPath ?? '');

    /** "STEPS Elite NJ's" / "Your club's" — the lede's subject. */
    readonly ledeOwner = computed(() => {
        const club = this.clubName().trim();
        return club ? `${club}'s` : "Your club's";
    });

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly actionInProgress = signal(false);
    readonly clubName = signal('');

    private readonly _clubTeams = signal<ClubTeamDto[]>([]);
    private readonly _registered = signal<RegisteredTeamDto[]>([]);
    private readonly _history = signal<ClubTeamEventHistoryDto[]>([]);

    private readonly registeredByClubTeam = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this._registered()) if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        return map;
    });

    /** History grouped per team, THIS event pinned first — it is a chip like any other, only highlighted. */
    private readonly historyByClubTeam = computed(() => {
        const here = this.jobPath().toLowerCase();
        const map = new Map<number, ClubTeamEventHistoryDto[]>();
        for (const h of this._history()) {
            const list = map.get(h.clubTeamId) ?? [];
            if (h.jobPath.toLowerCase() === here) list.unshift(h); else list.push(h);
            map.set(h.clubTeamId, list);
        }
        // One chip per event: a job with several Teams rows for this team keeps its best one.
        for (const [id, list] of map) map.set(id, collapseHistoryPerEvent(list));
        return map;
    });

    private buildRow(team: ClubTeamDto): LibraryRow {
        return {
            team,
            registered: this.registeredByClubTeam().get(team.clubTeamId) ?? null,
            history: this.historyByClubTeam().get(team.clubTeamId) ?? [],
        };
    }

    /** Active library, alphabetical. */
    readonly rows = computed<LibraryRow[]>(() =>
        this._clubTeams()
            .filter(t => !t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName))
            .map(t => this.buildRow(t)),
    );

    readonly archivedRows = computed<LibraryRow[]>(() =>
        this._clubTeams()
            .filter(t => t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName))
            .map(t => this.buildRow(t)),
    );

    /**
     * Short event names that two different jobs share ("Summer 2027" at LFTC and at Lax By The
     * Sea). A chip carrying only the tail would read as the same event; those get the organizer
     * back. Computed over the whole library, not per row, so the same event reads the same way
     * on every row.
     */
    private readonly ambiguousEventTails = computed<ReadonlySet<string>>(() => {
        const jobsByTail = new Map<string, Set<string>>();
        for (const h of this._history()) {
            const tail = this.eventTail(h.jobName).toLowerCase();
            const jobs = jobsByTail.get(tail) ?? new Set<string>();
            jobs.add(h.jobId);
            jobsByTail.set(tail, jobs);
        }
        const out = new Set<string>();
        for (const [tail, jobs] of jobsByTail) if (jobs.size > 1) out.add(tail);
        return out;
    });

    // ── UI state ───────────────────────────────────────────────────────
    readonly showArchived = signal(true);
    /** Rows whose full history list is unfolded (default shows the first few chips). */
    readonly expandedHistory = signal<ReadonlySet<number>>(new Set());
    readonly historyPreviewCount = 3;

    readonly editingTeam = signal<ClubTeamDto | null>(null);
    readonly showAddModal = signal(false);
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    readonly pendingRestore = signal<ClubTeamDto | null>(null);

    readonly formatLop = formatLop;

    ngOnInit(): void {
        this.load(true);
    }

    // ── Actions ────────────────────────────────────────────────────────
    /** A locked action, clicked: say why. The tooltip says the same, but touch has no tooltip. */
    explainLock(reason: string): void {
        this.toast.show(reason, 'warning', 3000);
    }

    toggleHistory(teamId: number): void {
        const next = new Set(this.expandedHistory());
        if (next.has(teamId)) next.delete(teamId); else next.add(teamId);
        this.expandedHistory.set(next);
    }

    /** The chip for the event the rep is signed in to — highlighted, never a column. */
    isHere(h: ClubTeamEventHistoryDto): boolean {
        return h.jobPath.toLowerCase() === this.jobPath().toLowerCase();
    }

    /** "Summer 2027", or "LFTC Summer 2027" when another organizer ran a "Summer 2027" too. */
    eventLabel(h: ClubTeamEventHistoryDto): string {
        const tail = this.eventTail(h.jobName);
        if (!this.ambiguousEventTails().has(tail.toLowerCase())) return tail;
        const idx = h.jobName.indexOf(':');
        const org = idx > 0 ? h.jobName.substring(0, idx).trim() : '';
        return org ? `${org} ${tail}` : tail;
    }

    /** Same org-prefix strip as eventName(), for a stored job name. */
    private eventTail(jobName: string): string {
        const idx = jobName.indexOf(':');
        return idx > 0 ? jobName.substring(idx + 1).trim() : jobName;
    }

    /**
     * The name a team carried at an event, ONLY when it tells the rep something. Older events
     * stored "{Club} {LibraryName}" (the pre-library convention), so a chip reading
     * "as Atlantic County Wave 2028" on a row named "2028" under the Atlantic County Wave badge
     * is noise on every row. A rename to anything else is real and shows.
     */
    eventAlias(row: LibraryRow, eventTeamName: string | null | undefined): string | null {
        const alias = (eventTeamName ?? '').trim();
        if (!alias) return null;
        const norm = (s: string) => s.replace(/\s+/g, ' ').trim().toLowerCase();
        const lib = norm(row.team.clubTeamName ?? '');
        if (norm(alias) === lib) return null;
        if (norm(alias) === norm(`${this.clubName()} ${row.team.clubTeamName ?? ''}`)) return null;
        return alias;
    }

    // ── Lock reasons — the shared rules (shared/teams/club-team-locks.ts), so this page and the
    //    fly-in can never disagree. Here a registration is named as a fact ("Registered for the
    //    Fall Rodeo 2026"), never as "here".
    private lockContext(row: LibraryRow): ClubTeamLockContext {
        return { registeredHere: !!row.registered, eventLabel: `the ${this.eventName()}` };
    }
    editLockReason(row: LibraryRow): string | null { return clubTeamEditLockReason(row.team, this.lockContext(row)); }
    archiveLockReason(row: LibraryRow): string | null { return clubTeamArchiveLockReason(this.lockContext(row)); }
    deleteLockReason(row: LibraryRow): string | null { return clubTeamDeleteLockReason(row.team, this.lockContext(row)); }
    removalFor(row: LibraryRow): ClubTeamRemoval { return clubTeamRemoval(row.team, this.lockContext(row)); }

    // ── Edit (name, grad year, level of play — library only) / Add ─────
    // No Rename here (Todd 2026-09-24): the name is one of the details. The event copy is renamed
    // on the Teams step, whose pencil may offer to update the library. Never the reverse.
    openEdit(row: LibraryRow): void {
        if (this.editLockReason(row)) return;
        this.editingTeam.set(row.team);
    }

    /** For the edit modal's "add as a new team instead" follow-up: may the OLD row be archived? */
    readonly editingArchiveLock = computed(() => {
        const editing = this.editingTeam();
        const row = editing ? this.rows().find(r => r.team.clubTeamId === editing.clubTeamId) : undefined;
        return row ? this.archiveLockReason(row) : null;
    });

    onTeamEdited(): void {
        this.editingTeam.set(null);
        this.load(false);
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.load(false);
    }

    // ── Archive / Restore / Delete ─────────────────────────────────────
    askArchive(row: LibraryRow): void {
        if (this.archiveLockReason(row)) return;
        this.pendingArchive.set(row.team);
    }

    confirmArchive(): void {
        const team = this.pendingArchive();
        if (!team) return;
        this.pendingArchive.set(null);
        this.actionInProgress.set(true);
        this.teamReg.archiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => this.load(false, () => this.toast.show(`${team.clubTeamName} archived.`, 'success', 3000)),
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.toast.show(extractHttpErrorMessage(err, 'Failed to archive team.'), 'danger', 5000);
                },
            });
    }

    askRestore(row: LibraryRow): void {
        if (!row.team.bArchived) return;
        this.pendingRestore.set(row.team);
    }

    confirmRestore(): void {
        const team = this.pendingRestore();
        if (!team) return;
        this.pendingRestore.set(null);
        this.actionInProgress.set(true);
        this.teamReg.unarchiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => this.load(false, () => this.toast.show(`${team.clubTeamName} restored to your library.`, 'success', 3000)),
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.toast.show(extractHttpErrorMessage(err, 'Failed to restore team.'), 'danger', 5000);
                },
            });
    }

    askDelete(row: LibraryRow): void {
        if (this.deleteLockReason(row)) return;
        this.pendingDelete.set(row.team);
    }

    confirmDelete(): void {
        const team = this.pendingDelete();
        if (!team) return;
        this.pendingDelete.set(null);
        this.actionInProgress.set(true);
        this.teamReg.deleteClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => this.load(false, () => this.toast.show(`${team.clubTeamName} deleted from your library.`, 'success', 3000)),
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.toast.show(extractHttpErrorMessage(err, 'Failed to delete team.'), 'danger', 5000);
                },
            });
    }

    // ── Load ───────────────────────────────────────────────────────────

    /**
     * Two sequential reads: metadata (library + this event's registrations, for the locks), then history. Releases
     * actionInProgress only once BOTH have landed, for the same reason the wizard holds it
     * — a control re-enabled while the old rows are still on screen invites a double click.
     * `onLoaded` (a success toast) runs after the new state is rendered, never before.
     */
    private load(showSpinner: boolean, onLoaded?: () => void): void {
        if (showSpinner) this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.clubName.set(meta.clubName || '');
                    this._clubTeams.set(meta.clubTeams || []);
                    this._registered.set(meta.registeredTeams || []);
                    this.teamReg.getClubTeamHistory()
                        .pipe(takeUntilDestroyed(this.destroyRef))
                        .subscribe({
                            next: (history) => {
                                this._history.set(history || []);
                                this.loading.set(false);
                                this.actionInProgress.set(false);
                                onLoaded?.();
                            },
                            error: () => {
                                // History is enrichment; the library itself is still usable without it.
                                this._history.set([]);
                                this.loading.set(false);
                                this.actionInProgress.set(false);
                                onLoaded?.();
                            },
                        });
                },
                error: () => {
                    this.loading.set(false);
                    this.actionInProgress.set(false);
                    this.error.set('Failed to load your Club Team Library.');
                },
            });
    }
}
