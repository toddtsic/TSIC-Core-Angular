import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RegisteredTeamsGridComponent } from '../components/registered-teams-grid.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { AgeGroupPickerModalComponent, type AgeGroupSelection } from './age-group-picker-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { LibraryFlyinComponent } from '../components/library-flyin.component';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';

interface AgePickerTeam {
    clubTeamId: number;
    clubTeamName: string;
    gradYear: string;
    levelOfPlay: string;
    currentAgeGroupId?: string;
}

/**
 * Teams step — single screen combining library management + event registration.
 * Assigning an age group IS the registration act. No separate review step needed.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [RegisteredTeamsGridComponent, TeamFormModalComponent, AgeGroupPickerModalComponent, ConfirmDialogComponent, LibraryFlyinComponent],
    template: `
    @if (loading()) {
      <div class="text-center py-4">
        <div class="spinner-border text-primary" role="status">
          <span class="visually-hidden">Loading teams...</span>
        </div>
      </div>
    } @else if (error()) {
      <div class="alert alert-danger">{{ error() }}</div>
    } @else {

      <!-- ── Registered teams for THIS event (single primary card) ── -->
      <div class="step-card step-card-registered">
        <div class="section-header section-registered">
          <i class="bi bi-check-circle-fill me-1"></i>
          Registered Teams ({{ enteredTeams().length }})
          <button type="button" class="btn btn-outline-primary btn-sm ms-auto section-action"
                  (click)="openLibraryFlyin()">
            <i class="bi bi-plus-circle me-1"></i>Register a <strong>Library Team</strong> for <strong>this Event</strong>
          </button>
        </div>

        @if (enteredTeams().length === 0) {
          <div class="wizard-empty-state" style="padding: var(--space-6) var(--space-4)">
            <i class="bi bi-clipboard-plus"></i>
            @if (allLibraryTeams().length === 0) {
              <strong>Two quick steps to register</strong>
              <span>
                First add teams to your <strong>club library</strong> (saved across all TSIC events),
                then register them for <strong>this event</strong>.
                Tap <strong>Register a Library Team for this Event</strong> above &mdash;
                we'll prompt you to add your first <strong>library team</strong>.
              </span>
            } @else {
              <strong>No teams registered yet</strong>
              <span>
                Tap <strong>Register a Library Team for this Event</strong> above
                to register your first team.
              </span>
            }
          </div>
        } @else {
          <div style="padding: var(--space-2) var(--space-3)">
            <app-registered-teams-grid
              [teams]="enteredTeams()"
              [showDeposit]="!fullPaymentRequired() && anyDepositDue()"
              [showBalance]="!fullPaymentRequired() && anyBalanceDue()"
              [showOwed]="fullPaymentRequired() && anyOwed()"
              [showPaid]="anyPaid()"
              [showProcessing]="false"
              [showCcOwed]="false"
              [showCkOwed]="false"
              [showRegDate]="false"
              [showLop]="true"
              [showRemove]="canRemoveTeam()"
              [actionInProgress]="actionInProgress()"
              [frozenTeamCol]="true"
              [teamColWidth]="140"
              [gridHeight]="180"
              (removeTeam)="onRemoveTeam($event)" />
          </div>

          <div class="step-card-footer">
            <span class="footer-hint">
              <i class="bi bi-shield-check me-1"></i>Your library is saved across all events
            </span>
            <button type="button" class="btn btn-sm btn-success fw-semibold"
                    (click)="proceedToPayment.emit()">
              {{ proceedButtonLabel() }} <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </div>
        }
      </div>

      <!-- ── Library fly-in (left-side drawer; shows on demand) ── -->
      <app-library-flyin
        [isOpen]="showLibraryFlyin()"
        [clubTeams]="allLibraryTeams()"
        [canRegister]="canRegisterTeam()"
        [actionInProgress]="actionInProgress()"
        [enteredTeamIds]="enteredTeamIds()"
        (closed)="closeLibraryFlyin()"
        (register)="onFlyinRegister($event)"
        (addNew)="showAddModal.set(true)"
        (edit)="openEditModal($event)"
        (archive)="askArchiveTeam($event)"
        (delete)="askDeleteTeam($event)"
        (restore)="askRestoreTeam($event)" />
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [clubName]="clubName()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }

    @if (editingTeam(); as editing) {
      <app-team-form-modal
        [clubName]="clubName()"
        [editingTeam]="editing"
        (saved)="onTeamEdited()"
        (closed)="editingTeam.set(null)" />
    }

    @if (agePickerTeam(); as pickerTeam) {
      <app-age-group-picker-modal
        [teamName]="pickerTeam.clubTeamName"
        [eventName]="jobName()"
        [gradYear]="pickerTeam.gradYear"
        [levelOfPlay]="pickerTeam.levelOfPlay"
        [currentAgeGroupId]="pickerTeam.currentAgeGroupId ?? ''"
        [ageGroups]="ageGroups()"
        [lopOptions]="lopOptions()"
        (selected)="onModalAgeGroupSelected(pickerTeam, $event)"
        (closed)="agePickerTeam.set(null)" />
    }

    @if (pendingRemove()) {
      <confirm-dialog
        title="Remove Team"
        [message]="'Remove <strong>' + pendingRemove()!.teamName + '</strong> from this event?'"
        confirmLabel="Remove"
        confirmVariant="danger"
        (confirmed)="confirmRemove()"
        (cancelled)="cancelRemove()" />
    }

    @if (pendingDelete()) {
      <confirm-dialog
        title="Delete Team"
        [message]="'Permanently delete <strong>' + pendingDelete()!.clubTeamName + '</strong> from your library? This cannot be undone.'"
        confirmLabel="Delete"
        confirmVariant="danger"
        (confirmed)="confirmDelete()"
        (cancelled)="cancelDelete()" />
    }

    @if (pendingArchive()) {
      <confirm-dialog
        title="Archive Team"
        [message]="'Archive <strong>' + pendingArchive()!.clubTeamName + '</strong>? It will disappear from your library but keep its event history. You can restore it from the Archived section.'"
        confirmLabel="Archive"
        confirmVariant="primary"
        (confirmed)="confirmArchive()"
        (cancelled)="cancelArchive()" />
    }

    @if (pendingRestore()) {
      <confirm-dialog
        title="Restore Team"
        [message]="'Restore <strong>' + pendingRestore()!.clubTeamName + '</strong> to your active library?'"
        confirmLabel="Restore"
        confirmVariant="primary"
        (confirmed)="confirmRestore()"
        (cancelled)="cancelRestore()" />
    }

  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-4); }

      /* ── Step Card (single primary card — registered teams) ── */
      .step-card {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        background: var(--brand-surface);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .step-card-registered { border-left: 4px solid var(--bs-success); }

      .step-card-footer {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--border-color);
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .footer-hint {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      /* ── Section banner header ── */
      .section-header {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
      }

      .section-registered {
        color: var(--bs-success);
        background: rgba(var(--bs-success-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-success-rgb), 0.12);
      }

      /* Compact action button nested inside a .section-header banner */
      .section-action {
        margin-left: auto;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: none;
        letter-spacing: 0;
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .step-card-footer {
          padding: var(--space-2);
          flex-wrap: wrap;
          gap: var(--space-2);
        }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamTeamsStepComponent implements OnInit {
    readonly proceedToPayment = output<void>();

    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly destroyRef = inject(DestroyRef);

    readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? 'this event');

    /** Capability flags from job pulse — gate the Register/Remove UI controls. */
    readonly canRegisterTeam = this.state.canRegisterTeam;
    readonly canRemoveTeam = this.state.canRemoveTeam;

    /** Job-config flag — drives phase-aware grid column visibility. */
    readonly fullPaymentRequired = this.state.fullPaymentRequired;

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly lopOptions = signal<string[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);
    /** When set, the edit modal is open for this team. */
    readonly editingTeam = signal<ClubTeamDto | null>(null);
    /** When set, the delete-confirm dialog is open for this team. */
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    /** When set, the archive-confirm dialog is open for this team. */
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    /** When set, the restore-confirm dialog is open for this team. */
    readonly pendingRestore = signal<ClubTeamDto | null>(null);
    /** Library fly-in open state. Auto-opens once on entry when library is empty. */
    readonly showLibraryFlyin = signal(false);
    private libraryFlyinAutoOpened = false;

    /** Which team's age picker modal is open (null = closed). */
    readonly agePickerTeam = signal<AgePickerTeam | null>(null);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    /** All library teams: available + entered. */
    readonly allLibraryTeams = computed<ClubTeamDto[]>(() => {
        const available = this._clubTeams();
        const entered = this._registeredTeams();

        const enteredAsLibrary: ClubTeamDto[] = entered
            .filter(r => r.clubTeamId)
            .map(r => ({
                clubTeamId: r.clubTeamId!,
                clubTeamName: r.teamName,
                clubTeamGradYear: r.ageGroupName,
                clubTeamLevelOfPlay: r.levelOfPlay ?? '',
                bHasBeenScheduled: r.bHasBeenScheduled,
                // Registered-for-this-event rows are by definition not archived on display.
                bArchived: false,
            }));

        // Merge: available first, then entered (deduplicated)
        const availableIds = new Set(available.map(t => t.clubTeamId));
        const enteredOnly = enteredAsLibrary.filter(t => !availableIds.has(t.clubTeamId));

        return [...available, ...enteredOnly];
    });

    /** Entered teams (registered for this event). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    /** Set of clubTeamIds that are entered — flyin uses this to mark rows as Registered. */
    readonly enteredTeamIds = computed(() =>
        new Set(this._registeredTeams().map(r => r.clubTeamId).filter((id): id is number => id != null)),
    );

    // Phase-aware grid column gating — derived from per-team data + bTeamsFullPaymentRequired.
    readonly anyDepositDue = computed(() => this._registeredTeams().some(t => t.depositDue > 0));
    readonly anyBalanceDue = computed(() => this._registeredTeams().some(t => t.additionalDue > 0));
    readonly anyPaid       = computed(() => this._registeredTeams().some(t => t.paidTotal > 0));
    readonly anyOwed       = computed(() => this._registeredTeams().some(t => t.owedTotal > 0));

    /** Count of teams not yet paid for — drives the Proceed-to-Payment label. */
    readonly newTeamsCount = computed(() => this._registeredTeams().filter(t => t.paidTotal === 0).length);

    readonly proceedButtonLabel = computed(() => {
        const n = this.newTeamsCount();
        if (n === 0) return 'Proceed to Payment';
        const noun = n === 1 ? 'team' : 'teams';
        return `Submit the ${n} new ${noun} and Proceed to Payment`;
    });

    ngOnInit(): void {
        this.loadTeamsMetadata(true);
    }

    isEnteredTeam(clubTeamId: number): boolean {
        return this._registeredTeams().some(r => r.clubTeamId === clubTeamId);
    }

    getEnteredInfo(clubTeamId: number): RegisteredTeamDto | null {
        return this._registeredTeams().find(r => r.clubTeamId === clubTeamId) ?? null;
    }

    /** Open age picker modal for a team. */
    openAgePicker(team: AgePickerTeam, paid: boolean): void {
        if (paid) return;
        this.agePickerTeam.set(team);
    }

    /** Library fly-in open/close. */
    openLibraryFlyin(): void { this.showLibraryFlyin.set(true); }
    closeLibraryFlyin(): void { this.showLibraryFlyin.set(false); }

    /** Flyin emits a ClubTeamDto when the user clicks Register; route to age picker. */
    onFlyinRegister(team: ClubTeamDto): void {
        this.openAgePicker({
            clubTeamId: team.clubTeamId,
            clubTeamName: team.clubTeamName,
            gradYear: team.clubTeamGradYear,
            levelOfPlay: team.clubTeamLevelOfPlay,
        }, false);
    }

    /** Handle age group + LOP selection from the modal. */
    onModalAgeGroupSelected(pickerTeam: AgePickerTeam, selection: AgeGroupSelection): void {
        this.agePickerTeam.set(null);

        // Build a ClubTeamDto-compatible object with the modal-selected LOP.
        // bHasBeenScheduled / bArchived are irrelevant here (registration path, not edit/delete).
        const team: ClubTeamDto = {
            clubTeamId: pickerTeam.clubTeamId,
            clubTeamName: pickerTeam.clubTeamName,
            clubTeamGradYear: pickerTeam.gradYear,
            clubTeamLevelOfPlay: selection.levelOfPlay || pickerTeam.levelOfPlay,
            bHasBeenScheduled: false,
            bArchived: false,
        };
        this.onSelectAgeGroup(team, selection.ageGroupId);
    }

    /** Register (or re-register) a team with the selected age group. */
    onSelectAgeGroup(team: ClubTeamDto, ageGroupId: string): void {
        const existing = this.getEnteredInfo(team.clubTeamId);

        // If already registered with same age group, no-op
        if (existing && existing.ageGroupId === ageGroupId) {
            return;
        }

        this.actionInProgress.set(true);

        const doRegister = () => {
            this.teamReg.registerTeamForEvent({
                clubTeamId: team.clubTeamId,
                ageGroupId,
                teamName: team.clubTeamName,
                clubTeamGradYear: team.clubTeamGradYear,
                levelOfPlay: team.clubTeamLevelOfPlay || undefined,
            })
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: (resp) => {
                        this.actionInProgress.set(false);
                        if (!resp.success) {
                            this.toast.show(resp.message || 'Registration failed.', 'danger', 6000);
                            return;
                        }
                        const msg = resp.isWaitlisted
                            ? `${team.clubTeamName} waitlisted for ${resp.waitlistAgegroupName ?? ''}`
                            : `${team.clubTeamName} registered for the event!`;
                        this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000);
                        // Close the flyin so the user sees the team land in the registered list.
                        this.showLibraryFlyin.set(false);
                        this.loadTeamsMetadata();
                    },
                    error: () => {
                        this.actionInProgress.set(false);
                        this.toast.show('Failed to register team.', 'danger', 4000);
                        this.loadTeamsMetadata();
                    },
                });
        };

        // If changing age group, unregister first then re-register
        if (existing) {
            this.teamReg.unregisterTeamFromEvent(existing.teamId)
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({ next: doRegister, error: () => {
                    this.actionInProgress.set(false);
                    this.toast.show('Failed to change age group.', 'danger', 4000);
                    this.loadTeamsMetadata();
                }});
        } else {
            doRegister();
        }
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.loadTeamsMetadata();
    }

    /** Open the shared modal in edit mode for a library team. */
    openEditModal(team: ClubTeamDto): void {
        if (team.bHasBeenScheduled) return;
        this.editingTeam.set(team);
    }

    onTeamEdited(): void {
        this.editingTeam.set(null);
        this.loadTeamsMetadata();
    }

    askDeleteTeam(team: ClubTeamDto): void {
        if (team.bHasBeenScheduled) return;
        this.pendingDelete.set(team);
    }

    confirmDelete(): void {
        const team = this.pendingDelete();
        if (!team) return;
        this.pendingDelete.set(null);
        this.actionInProgress.set(true);

        this.teamReg.deleteClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} deleted from library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to delete team.', 'danger', 5000);
                },
            });
    }

    cancelDelete(): void {
        this.pendingDelete.set(null);
    }

    askArchiveTeam(team: ClubTeamDto): void {
        if (!team.bHasBeenScheduled || this.isEnteredTeam(team.clubTeamId)) return;
        this.pendingArchive.set(team);
    }

    confirmArchive(): void {
        const team = this.pendingArchive();
        if (!team) return;
        this.pendingArchive.set(null);
        this.actionInProgress.set(true);

        this.teamReg.archiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} archived from library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to archive team.', 'danger', 5000);
                },
            });
    }

    cancelArchive(): void {
        this.pendingArchive.set(null);
    }

    askRestoreTeam(team: ClubTeamDto): void {
        if (!team.bArchived) return;
        this.pendingRestore.set(team);
    }

    confirmRestore(): void {
        const team = this.pendingRestore();
        if (!team) return;
        this.pendingRestore.set(null);
        this.actionInProgress.set(true);

        this.teamReg.unarchiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} restored to library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to restore team.', 'danger', 5000);
                },
            });
    }

    cancelRestore(): void {
        this.pendingRestore.set(null);
    }

    pendingRemove = signal<RegisteredTeamDto | null>(null);

    onRemoveTeam(team: RegisteredTeamDto): void {
        if (team.paidTotal > 0) return;
        this.pendingRemove.set(team);
    }

    confirmRemove(): void {
        const team = this.pendingRemove();
        if (!team) return;
        this.pendingRemove.set(null);
        this.unregisterTeam(team.teamId, team.teamName);
    }

    cancelRemove(): void {
        this.pendingRemove.set(null);
    }

    // ── Private ─────────────────────────────────────────────────────

    private unregisterTeam(teamId: string, teamName: string): void {
        this.actionInProgress.set(true);

        this.teamReg.unregisterTeamFromEvent(teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${teamName} removed from event.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: () => {
                    this.actionInProgress.set(false);
                    this.toast.show('Failed to remove team.', 'danger', 4000);
                    this.loadTeamsMetadata();
                },
            });
    }

    private loadTeamsMetadata(showSpinner = false): void {
        if (showSpinner) this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this.clubName.set(meta.clubName || 'your club');
                    this._registeredTeams.set(meta.registeredTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    this.lopOptions.set(meta.lopOptions || []);
                    this.state.applyTeamsMetadata(meta);

                    // Auto-open the flyin once on entry when the library is empty —
                    // first-time clubs need a clear "add your first team" prompt.
                    if (!this.libraryFlyinAutoOpened
                        && (meta.clubTeams?.length ?? 0) === 0
                        && (meta.registeredTeams?.length ?? 0) === 0) {
                        this.showLibraryFlyin.set(true);
                        this.libraryFlyinAutoOpened = true;
                    }
                },
                error: () => {
                    this.loading.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
