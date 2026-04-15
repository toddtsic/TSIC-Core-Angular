import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { ClubRosterService } from './club-rosters.service';
import { TeamDropdownComponent } from './team-dropdown.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';
import type { ClubRosterPlayerDto } from '@core/api/models/ClubRosterPlayerDto';

@Component({
    selector: 'app-club-rosters',
    standalone: true,
    imports: [FormsModule, GridAllModule, TeamDropdownComponent, ConfirmDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './club-rosters.component.html',
    styleUrl: './club-rosters.component.scss'
})
export class ClubRostersComponent implements OnInit {
    private readonly rosterService = inject(ClubRosterService);
    private readonly toast = inject(ToastService);

    // Data
    readonly teams = signal<ClubRosterTeamDto[]>([]);
    readonly selectedTeamId = signal<string | null>(null);
    readonly roster = signal<ClubRosterPlayerDto[]>([]);

    // UI state
    readonly isLoading = signal(false);
    readonly isLoadingRoster = signal(false);
    readonly isMutating = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Move modal state
    readonly moveTarget = signal<ClubRosterPlayerDto | null>(null);
    readonly moveTargetTeamId = signal<string | null>(null);
    readonly editUniformNumber = signal<string>('');
    readonly isSavingUniform = signal(false);

    // Delete modal state
    readonly deleteTarget = signal<ClubRosterPlayerDto | null>(null);

    // Computed
    readonly selectedTeam = computed(() => {
        const id = this.selectedTeamId();
        return id ? this.teams().find(t => t.teamId === id) ?? null : null;
    });

    readonly otherTeams = computed(() => {
        const id = this.selectedTeamId();
        return this.teams().filter(t => t.teamId !== id);
    });

    ngOnInit(): void {
        this.loadTeams();
    }

    loadTeams(): void {
        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.rosterService.getTeams().subscribe({
            next: (teams) => {
                this.teams.set(teams);
                this.isLoading.set(false);
                if (!this.selectedTeamId() && teams.length > 0) {
                    this.selectTeam(teams[0].teamId);
                }
            },
            error: (err) => {
                this.isLoading.set(false);
                this.errorMessage.set(err.error?.message || 'Failed to load teams.');
            }
        });
    }

    selectTeam(teamId: string): void {
        this.selectedTeamId.set(teamId);
        this.cancelMove();
        this.loadRoster(teamId);
    }

    private loadRoster(teamId: string): void {
        this.isLoadingRoster.set(true);
        this.rosterService.getRoster(teamId).subscribe({
            next: (roster) => {
                this.roster.set(roster);
                this.isLoadingRoster.set(false);
            },
            error: (err) => {
                this.isLoadingRoster.set(false);
                this.toast.show(err.error?.message || 'Failed to load roster.', 'danger');
            }
        });
    }

    // ── Move ──

    startMove(player: ClubRosterPlayerDto): void {
        this.moveTarget.set(player);
        this.moveTargetTeamId.set(null);
        this.editUniformNumber.set(player.uniformNumber ?? '');
    }

    cancelMove(): void {
        this.moveTarget.set(null);
        this.moveTargetTeamId.set(null);
    }

    onMoveTeamSelected(teamId: string): void {
        this.moveTargetTeamId.set(teamId);
    }

    confirmMove(): void {
        const player = this.moveTarget();
        const target = this.moveTargetTeamId();
        if (!player || !target) return;

        this.isMutating.set(true);
        this.rosterService.movePlayers({
            registrationIds: [player.registrationId],
            targetTeamId: target
        }).subscribe({
            next: (result) => {
                this.isMutating.set(false);
                this.toast.show(result.message, 'success');
                this.cancelMove();
                this.refreshAfterMutation();
            },
            error: (err) => {
                this.isMutating.set(false);
                this.toast.show(err.error?.message || 'Move failed.', 'danger');
            }
        });
    }

    saveUniformNumber(): void {
        const player = this.moveTarget();
        if (!player) return;

        const newValue = this.editUniformNumber().trim() || null;
        if (newValue === (player.uniformNumber ?? null)) return;

        this.isSavingUniform.set(true);
        this.rosterService.updateUniformNumber({
            registrationId: player.registrationId,
            uniformNumber: newValue
        }).subscribe({
            next: () => {
                this.isSavingUniform.set(false);
                this.toast.show('Uniform number updated.', 'success');
                // Update the roster signal in-place
                this.roster.set(this.roster().map(p =>
                    p.registrationId === player.registrationId
                        ? { ...p, uniformNumber: newValue } : p
                ));
                this.moveTarget.set({ ...player, uniformNumber: newValue });
            },
            error: (err) => {
                this.isSavingUniform.set(false);
                this.toast.show(err.error?.message || 'Failed to update uniform number.', 'danger');
            }
        });
    }

    // ── Delete ──

    startDelete(player: ClubRosterPlayerDto): void {
        this.deleteTarget.set(player);
    }

    onDeleteConfirmed(): void {
        const player = this.deleteTarget();
        if (!player) return;

        this.deleteTarget.set(null);
        this.isMutating.set(true);

        this.rosterService.deletePlayers({
            registrationIds: [player.registrationId]
        }).subscribe({
            next: (result) => {
                this.isMutating.set(false);
                this.toast.show(result.message, 'success');
                this.refreshAfterMutation();
            },
            error: (err) => {
                this.isMutating.set(false);
                this.toast.show(err.error?.message || 'Delete failed.', 'danger');
            }
        });
    }

    onDeleteCancelled(): void {
        this.deleteTarget.set(null);
    }

    private refreshAfterMutation(): void {
        this.loadTeams();
        const teamId = this.selectedTeamId();
        if (teamId) this.loadRoster(teamId);
    }
}
