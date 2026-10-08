import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import type { ClubTeamDto, RegisteredTeamDto, TeamsMetadataResponse } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { TeamFormModalComponent } from '@views/registration/team/steps/team-form-modal.component';
import { LibraryPanelComponent } from '@views/registration/team/components/library-panel.component';

/**
 * Club Team Library — the club's list of teams, and NOTHING about registering for the event the
 * rep happens to be signed in to (Todd 2026-09-24: "you are updating your list of team options
 * available when you register, you are not registering here").
 *
 * It IS the Teams step's library panel (Ann/Todd 2026-09-27: "look identical to the teams tab,
 * familiarity is important"): the same component in 'page' mode — one list of every active team,
 * Archived kept, no event history, no Registered pile, no event notation on a row (AR-152). A team
 * registered for the signed-in event shows only a greyed Archive, which names the event.
 *
 * Job-scoped by ruling (Todd, 2026-09-22): auth is job-scoped, so the page still knows this event —
 * it uses that only for the archive lock. Every mutation goes through the same service calls as
 * the wizard, so the two surfaces can never disagree about what a team is.
 */
@Component({
    selector: 'app-club-library',
    standalone: true,
    imports: [ConfirmDialogComponent, TeamFormModalComponent, LibraryPanelComponent],
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

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly actionInProgress = signal(false);
    readonly clubName = signal('');

    readonly clubTeams = signal<ClubTeamDto[]>([]);
    readonly registered = signal<RegisteredTeamDto[]>([]);

    readonly showAddModal = signal(false);
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    readonly pendingRestore = signal<ClubTeamDto | null>(null);

    ngOnInit(): void {
        this.load(true);
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.load(false);
    }

    // ── Archive / Restore / Delete — the panel has already applied the shared locks ──
    confirmArchive(): void {
        const team = this.pendingArchive();
        if (!team) return;
        this.pendingArchive.set(null);
        this.run(this.teamReg.archiveClubTeam(team.clubTeamId), `${team.clubTeamName} archived.`, 'Failed to archive team.');
    }

    confirmRestore(): void {
        const team = this.pendingRestore();
        if (!team) return;
        this.pendingRestore.set(null);
        this.run(this.teamReg.unarchiveClubTeam(team.clubTeamId), `${team.clubTeamName} restored to your library.`, 'Failed to restore team.');
    }

    confirmDelete(): void {
        const team = this.pendingDelete();
        if (!team) return;
        this.pendingDelete.set(null);
        this.run(this.teamReg.deleteClubTeam(team.clubTeamId), `${team.clubTeamName} deleted from your library.`, 'Failed to delete team.');
    }

    private run(call: Observable<unknown>, done: string, failed: string): void {
        this.actionInProgress.set(true);
        call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: () => this.load(false, () => this.toast.show(done, 'success', 3000)),
            error: (err: unknown) => {
                this.actionInProgress.set(false);
                this.toast.show(extractHttpErrorMessage(err, failed), 'danger', 5000);
            },
        });
    }

    // ── Load ───────────────────────────────────────────────────────────

    /**
     * One read: the wizard's metadata (library + this event's registrations, for the archive lock).
     * Releases actionInProgress only once it has landed — a control re-enabled while the old rows
     * are still on screen invites a double click. `onLoaded` (a success toast) runs after the new
     * state is set, never before.
     */
    load(showSpinner: boolean, onLoaded?: () => void): void {
        if (showSpinner) this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.clubName.set(meta.clubName || '');
                    this.clubTeams.set(meta.clubTeams || []);
                    this.registered.set(meta.registeredTeams || []);
                    this.loading.set(false);
                    this.actionInProgress.set(false);
                    onLoaded?.();
                },
                error: () => {
                    this.loading.set(false);
                    this.actionInProgress.set(false);
                    this.error.set('Failed to load your Club Team Library.');
                },
            });
    }
}
