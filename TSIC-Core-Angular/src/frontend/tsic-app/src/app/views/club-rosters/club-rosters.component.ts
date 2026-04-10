import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit, ViewChild } from '@angular/core';
import { GridAllModule, GridComponent, SelectionSettingsModel } from '@syncfusion/ej2-angular-grids';
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

    @ViewChild('rosterGrid') grid!: GridComponent;

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

    // Grid config
    readonly selectionSettings: SelectionSettingsModel = { type: 'Multiple', checkboxOnly: true };

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

    onRowSelected(): void {
        this.syncSelection();
    }

    onRowDeselected(): void {
        this.syncSelection();
    }

    private syncSelection(): void {
        if (!this.grid) return;
        const selected = this.grid.getSelectedRecords() as ClubRosterPlayerDto[];
        this.selectedRegIds.set(new Set(selected.map(r => r.registrationId)));
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
                this.refreshAfterMutation();
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
                this.refreshAfterMutation();
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

    private refreshAfterMutation(): void {
        this.loadTeams();
        const teamId = this.selectedTeamId();
        if (teamId) this.loadRoster(teamId);
    }
}
