import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { ClubRosterService } from './club-rosters.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';
import type { ClubRosterPlayerDto } from '@core/api/models/ClubRosterPlayerDto';

@Component({
    selector: 'app-club-rosters',
    standalone: true,
    imports: [GridAllModule, ConfirmDialogComponent],
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
    readonly selectedRegIds = signal<Set<string>>(new Set());

    // UI state
    readonly isLoading = signal(false);
    readonly isLoadingRoster = signal(false);
    readonly isMutating = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Move target
    readonly moveTargetTeamId = signal<string | null>(null);

    // Confirm dialog
    readonly showDeleteConfirm = signal(false);

    // Computed
    readonly selectedTeam = computed(() => {
        const id = this.selectedTeamId();
        return id ? this.teams().find(t => t.teamId === id) ?? null : null;
    });

    readonly otherTeams = computed(() => {
        const id = this.selectedTeamId();
        return this.teams().filter(t => t.teamId !== id);
    });

    readonly hasSelection = computed(() => this.selectedRegIds().size > 0);
    readonly selectedCount = computed(() => this.selectedRegIds().size);

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

                // Auto-select first team if none selected
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
        this.selectedRegIds.set(new Set());
        this.moveTargetTeamId.set(null);
        this.loadRoster(teamId);
    }

    onTeamChange(event: Event): void {
        const select = event.target as HTMLSelectElement;
        if (select.value) {
            this.selectTeam(select.value);
        }
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

    togglePlayer(regId: string): void {
        const ids = new Set(this.selectedRegIds());
        if (ids.has(regId)) {
            ids.delete(regId);
        } else {
            ids.add(regId);
        }
        this.selectedRegIds.set(ids);
    }

    toggleAll(): void {
        if (this.selectedRegIds().size === this.roster().length) {
            this.selectedRegIds.set(new Set());
        } else {
            this.selectedRegIds.set(new Set(this.roster().map(r => r.registrationId)));
        }
    }

    isSelected(regId: string): boolean {
        return this.selectedRegIds().has(regId);
    }

    get allSelected(): boolean {
        return this.roster().length > 0 && this.selectedRegIds().size === this.roster().length;
    }

    onMoveTargetChange(event: Event): void {
        const select = event.target as HTMLSelectElement;
        this.moveTargetTeamId.set(select.value || null);
    }

    movePlayers(): void {
        const target = this.moveTargetTeamId();
        if (!target || this.selectedRegIds().size === 0) return;

        this.isMutating.set(true);
        this.rosterService.movePlayers({
            registrationIds: Array.from(this.selectedRegIds()),
            targetTeamId: target
        }).subscribe({
            next: (result) => {
                this.isMutating.set(false);
                this.toast.show(result.message, 'success');
                this.selectedRegIds.set(new Set());
                this.moveTargetTeamId.set(null);
                this.loadTeams();
                this.loadRoster(this.selectedTeamId()!);
            },
            error: (err) => {
                this.isMutating.set(false);
                this.toast.show(err.error?.message || 'Move failed.', 'danger');
            }
        });
    }

    confirmDelete(): void {
        if (this.selectedRegIds().size === 0) return;
        this.showDeleteConfirm.set(true);
    }

    onDeleteConfirmed(): void {
        this.showDeleteConfirm.set(false);
        this.isMutating.set(true);

        this.rosterService.deletePlayers({
            registrationIds: Array.from(this.selectedRegIds())
        }).subscribe({
            next: (result) => {
                this.isMutating.set(false);
                this.toast.show(result.message, 'success');
                this.selectedRegIds.set(new Set());
                this.loadTeams();
                this.loadRoster(this.selectedTeamId()!);
            },
            error: (err) => {
                this.isMutating.set(false);
                this.toast.show(err.error?.message || 'Delete failed.', 'danger');
            }
        });
    }

    onDeleteCancelled(): void {
        this.showDeleteConfirm.set(false);
    }
}
