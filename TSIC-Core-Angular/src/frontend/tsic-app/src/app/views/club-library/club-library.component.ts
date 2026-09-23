import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import type { AgeGroupDto, ClubTeamDto, ClubTeamEventHistoryDto, RegisteredTeamDto, TeamsMetadataResponse } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { TeamRenameConfirmComponent, type TeamRenameConfirmation } from '@shared/teams/team-rename-confirm.component';
import { formatLop } from '@shared/teams/lop-choices';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { TeamFormModalComponent } from '@views/registration/team/steps/team-form-modal.component';
import { isTeamOfferedAtEvent, resolveOldestOfferedGradYear } from '@views/registration/team/components/event-age-group.util';
import { RegisterTeamDialogComponent, type RegisterTeamPick } from './register-team-dialog.component';

/** A library team's relationship to THIS event — the status column, never inferred by the template. */
export type LibraryRowStatus = 'registered' | 'waitlisted' | 'dropped' | 'available' | 'outside' | 'closed';

/** One row of the library table: the library entry plus everything the page knows about it here. */
export interface LibraryRow {
    team: ClubTeamDto;
    status: LibraryRowStatus;
    /** This event's copy when registered or waitlisted here (the unregister / rename target). */
    registered: RegisteredTeamDto | null;
    /** This event's copy when a director dropped it — history, not a live entry. */
    dropped: RegisteredTeamDto | null;
    /** Other events this team has been registered in, newest first. This event is excluded. */
    history: ClubTeamEventHistoryDto[];
}

/** Which side of the two-name model the rename dialog was opened from (mirrors the wizard). */
type PendingRename =
    | { origin: 'event'; team: RegisteredTeamDto }
    | { origin: 'library'; team: ClubTeamDto };

/**
 * Club Team Library — the club rep's standalone home for their library, independent of
 * registering for an event. The wizard's fly-in is a picker inside a registration; this
 * page is the library itself: every team, its status for THIS event, where else it has
 * played, and every housekeeping action on every row (the fly-in hides the kebab on
 * registered rows; here it never does).
 *
 * Job-scoped by ruling (Todd, 2026-09-22): auth is job-scoped, so status is for the event
 * the rep is signed in to, and history is cross-event. The data comes from the same two
 * reads the wizard uses (metadata) plus one history endpoint; every mutation goes through
 * the same service calls, so the two surfaces can never disagree about what a team is.
 */
@Component({
    selector: 'app-club-library',
    standalone: true,
    imports: [ConfirmDialogComponent, TeamRenameConfirmComponent, TeamFormModalComponent, RegisterTeamDialogComponent],
    templateUrl: './club-library.component.html',
    styleUrl: './club-library.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubLibraryComponent implements OnInit {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly pulseService = inject(JobPulseService);
    private readonly router = inject(Router);
    private readonly destroyRef = inject(DestroyRef);

    /** Same org-prefix strip as the wizard, so the page names the event the way the wizard does. */
    readonly eventName = computed(() => {
        const raw = this.jobService.currentJob()?.jobName ?? 'this event';
        const idx = raw.indexOf(':');
        return idx > 0 ? raw.substring(idx + 1).trim() : raw;
    });

    readonly jobPath = computed(() => this.jobService.currentJob()?.jobPath ?? '');

    // Capability flags — the SAME three rules TeamWizardStateService derives, from the same pulse.
    readonly canRegister = computed(() => {
        const p = this.pulseService.pulse();
        return !!p && p.teamRegistrationOpen && p.clubRepAllowAdd;
    });
    readonly canRemove = computed(() => {
        const p = this.pulseService.pulse();
        return !!p && p.teamRegistrationOpen && p.clubRepAllowDelete;
    });
    readonly canEdit = computed(() => {
        const p = this.pulseService.pulse();
        return !!p && p.teamRegistrationOpen && p.clubRepAllowEdit;
    });

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly actionInProgress = signal(false);
    readonly clubName = signal('');
    readonly ageGroups = signal<AgeGroupDto[]>([]);

    private readonly _clubTeams = signal<ClubTeamDto[]>([]);
    private readonly _registered = signal<RegisteredTeamDto[]>([]);
    private readonly _dropped = signal<RegisteredTeamDto[]>([]);
    private readonly _history = signal<ClubTeamEventHistoryDto[]>([]);

    private readonly registeredByClubTeam = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this._registered()) if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        return map;
    });

    private readonly droppedByClubTeam = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this._dropped()) if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        return map;
    });

    /** History grouped per team, THIS event removed (its row is the status column). */
    private readonly historyByClubTeam = computed(() => {
        const here = this.jobPath().toLowerCase();
        const map = new Map<number, ClubTeamEventHistoryDto[]>();
        for (const h of this._history()) {
            if (h.jobPath.toLowerCase() === here) continue;
            const list = map.get(h.clubTeamId) ?? [];
            list.push(h);
            map.set(h.clubTeamId, list);
        }
        return map;
    });

    private readonly oldestOffered = computed(() => resolveOldestOfferedGradYear(this.ageGroups()));

    private buildRow(team: ClubTeamDto): LibraryRow {
        const registered = this.registeredByClubTeam().get(team.clubTeamId) ?? null;
        const dropped = this.droppedByClubTeam().get(team.clubTeamId) ?? null;
        let status: LibraryRowStatus;
        if (registered) status = registered.isWaitlisted ? 'waitlisted' : 'registered';
        else if (dropped) status = 'dropped';
        else if (!this.canRegister()) status = 'closed';
        else status = isTeamOfferedAtEvent(this.oldestOffered(), team.clubTeamGradYear) ? 'available' : 'outside';
        return { team, status, registered, dropped, history: this.historyByClubTeam().get(team.clubTeamId) ?? [] };
    }

    /** Active library, alphabetical — the status column carries the grouping the fly-in does by section. */
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

    readonly registeredCount = computed(() => this.rows().filter(r => r.status === 'registered' || r.status === 'waitlisted').length);
    readonly availableCount = computed(() => this.rows().filter(r => r.status === 'available').length);

    // ── UI state ───────────────────────────────────────────────────────
    readonly showArchived = signal(true);
    readonly openMenuTeamId = signal<number | null>(null);
    /** Rows whose full history list is unfolded (default shows the first few chips). */
    readonly expandedHistory = signal<ReadonlySet<number>>(new Set());
    readonly historyPreviewCount = 3;

    readonly registering = signal<ClubTeamDto | null>(null);
    readonly registerError = signal<string | null>(null);
    readonly pendingRename = signal<PendingRename | null>(null);
    readonly renameError = signal<string | null>(null);
    readonly editingTeam = signal<ClubTeamDto | null>(null);
    readonly showAddModal = signal(false);
    readonly pendingUnregister = signal<RegisteredTeamDto | null>(null);
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    readonly pendingRestore = signal<ClubTeamDto | null>(null);

    readonly formatLop = formatLop;

    ngOnInit(): void {
        this.load(true);
    }

    // ── Navigation ─────────────────────────────────────────────────────
    goToRegistration(step: 'teams' | 'payment' = 'teams'): void {
        const jobPath = this.jobPath();
        if (!jobPath) return;
        this.router.navigateByUrl(`/${jobPath}/registration/team?step=${step}`);
    }

    // ── Kebab ──────────────────────────────────────────────────────────
    toggleMenu(event: MouseEvent, teamId: number): void {
        event.stopPropagation();
        this.openMenuTeamId.set(this.openMenuTeamId() === teamId ? null : teamId);
    }
    closeMenu(): void { this.openMenuTeamId.set(null); }

    @HostListener('document:click')
    onDocumentClick(): void { if (this.openMenuTeamId() !== null) this.closeMenu(); }

    @HostListener('document:keydown.escape')
    onEscape(): void { this.closeMenu(); }

    toggleHistory(teamId: number): void {
        const next = new Set(this.expandedHistory());
        if (next.has(teamId)) next.delete(teamId); else next.add(teamId);
        this.expandedHistory.set(next);
    }

    /** Event label for a history chip — org prefix stripped, same as the page's own event name. */
    eventLabel(h: ClubTeamEventHistoryDto): string {
        const idx = h.jobName.indexOf(':');
        return idx > 0 ? h.jobName.substring(idx + 1).trim() : h.jobName;
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

    // ── Lock reasons — mirror the fly-in so the two surfaces never disagree ──
    editLockReason(row: LibraryRow): string | null {
        if (!this.canEdit()) return 'Editing is off for this event';
        if (row.team.bHasBeenScheduled) return 'Has event history';
        return null;
    }
    archiveLockReason(row: LibraryRow): string | null {
        return row.registered ? 'Registered for this event' : null;
    }
    deleteLockReason(row: LibraryRow): string | null {
        if (row.team.bHasEventRegistrations) return 'Use Archive — registered for an event';
        if (row.registered) return 'Registered for this event';
        return null;
    }
    registerLockReason(row: LibraryRow): string | null {
        if (row.registered) return 'Already registered';
        if (row.dropped) return 'Dropped by the director';
        if (!this.canRegister()) return 'Registration is closed';
        return null;
    }
    unregisterLockReason(row: LibraryRow): string | null {
        if (!row.registered) return 'Not registered here';
        if (!this.canRemove()) return 'Removal is off for this event';
        if (row.registered.paidTotal > 0) return 'Payment received — contact the event';
        return null;
    }

    // ── Register ───────────────────────────────────────────────────────
    openRegister(row: LibraryRow): void {
        this.closeMenu();
        if (this.registerLockReason(row)) return;
        this.registerError.set(null);
        this.registering.set(row.team);
    }

    confirmRegister(pick: RegisterTeamPick): void {
        this.registerError.set(null);
        this.actionInProgress.set(true);
        this.teamReg.registerTeamForEvent({
            clubTeamId: pick.team.clubTeamId,
            ageGroupId: pick.ageGroupId,
            teamName: pick.team.clubTeamName,
            clubTeamGradYear: pick.team.clubTeamGradYear,
            levelOfPlay: pick.levelOfPlay || pick.team.clubTeamLevelOfPlay || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    if (!resp.success) {
                        this.actionInProgress.set(false);
                        this.registerError.set(resp.message || 'Registration was not accepted.');
                        return;
                    }
                    this.registering.set(null);
                    const msg = resp.isWaitlisted
                        ? `${pick.team.clubTeamName} waitlisted for ${(resp.waitlistAgegroupName ?? '').replace(/^\s*WAITLIST\s*-\s*/i, '').trim() || 'the waitlist'}`
                        : `${pick.team.clubTeamName} registered for the ${this.eventName()}!`;
                    this.load(false, () => this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.registerError.set(extractHttpErrorMessage(err, 'Failed to register team.'));
                },
            });
    }

    // ── Unregister (this event only) ───────────────────────────────────
    askUnregister(row: LibraryRow): void {
        this.closeMenu();
        if (this.unregisterLockReason(row) || !row.registered) return;
        this.pendingUnregister.set(row.registered);
    }

    confirmUnregister(): void {
        const team = this.pendingUnregister();
        if (!team) return;
        this.pendingUnregister.set(null);
        this.actionInProgress.set(true);
        this.teamReg.unregisterTeamFromEvent(team.teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => this.load(false, () => this.toast.show(`${team.teamName} removed from the ${this.eventName()}.`, 'success', 3000)),
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.toast.show(extractHttpErrorMessage(err, 'Failed to remove team.'), 'danger', 5000);
                },
            });
    }

    // ── Rename (two-place dialog, same as the wizard) ──────────────────
    openRename(row: LibraryRow): void {
        this.closeMenu();
        this.renameError.set(null);
        this.pendingRename.set({ origin: 'library', team: row.team });
    }

    closeRename(): void {
        this.pendingRename.set(null);
        this.renameError.set(null);
    }

    renameEventName(p: PendingRename): string {
        if (p.origin === 'event') return p.team.teamName;
        return this.registeredByClubTeam().get(p.team.clubTeamId)?.teamName ?? '';
    }
    renameSeed(p: PendingRename): string {
        return p.origin === 'event' ? p.team.teamName : p.team.clubTeamName;
    }
    renameLibraryName(p: PendingRename): string | null {
        if (p.origin === 'library') return p.team.clubTeamName;
        if (p.team.clubTeamId == null) return null;
        return this._clubTeams().find(c => c.clubTeamId === p.team.clubTeamId)?.clubTeamName ?? null;
    }
    renameRegisteredHere(p: PendingRename): boolean {
        return p.origin === 'event' || this.registeredByClubTeam().has(p.team.clubTeamId);
    }

    confirmRename(c: TeamRenameConfirmation): void {
        const pending = this.pendingRename();
        if (!pending) return;
        this.renameError.set(null);
        this.actionInProgress.set(true);

        const call$ = pending.origin === 'event'
            ? this.teamReg.renameRegisteredTeam(pending.team.teamId, c.name, c.alsoPropagate, c.levelOfPlay)
            : this.teamReg.renameClubTeam(pending.team.clubTeamId, c.name, c.alsoPropagate);
        const oldName = pending.origin === 'event' ? pending.team.teamName : pending.team.clubTeamName;
        const where = pending.origin === 'event'
            ? (c.alsoPropagate ? 'in this event and your Club Team Library' : 'in this event')
            : (c.alsoPropagate ? 'in your Club Team Library and this event' : 'in your Club Team Library');

        call$.pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.closeRename();
                    this.load(false, () => this.toast.show(`${oldName} is now ${c.name} ${where}.`, 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.renameError.set(extractHttpErrorMessage(err, 'Failed to rename team.'));
                },
            });
    }

    // ── Edit details / Add ─────────────────────────────────────────────
    openEdit(row: LibraryRow): void {
        this.closeMenu();
        if (this.editLockReason(row)) return;
        this.editingTeam.set(row.team);
    }

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
        this.closeMenu();
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
        this.closeMenu();
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
        this.closeMenu();
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
     * Two sequential reads: metadata (library + this event), then history. Releases
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
                    this._dropped.set(meta.droppedTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
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
