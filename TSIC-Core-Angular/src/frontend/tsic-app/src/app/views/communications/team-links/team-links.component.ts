import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { TeamLinksService } from './services/team-links.service';
import { TeamLinkFormModalComponent, TeamLinkFormResult } from './components/team-link-form-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import type { AdminTeamLinkDto, TeamLinkTeamOptionDto } from '@core/api';

@Component({
    selector: 'app-team-links',
    standalone: true,
    imports: [GridAllModule, TeamLinkFormModalComponent, ConfirmDialogComponent],
    templateUrl: './team-links.component.html',
    styleUrl: './team-links.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamLinksComponent implements OnInit {
    private readonly service = inject(TeamLinksService);
    private readonly toast = inject(ToastService);

    readonly links = signal<AdminTeamLinkDto[]>([]);
    readonly availableTeams = signal<TeamLinkTeamOptionDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    readonly showAddModal = signal(false);
    readonly showEditModal = signal(false);
    readonly editTarget = signal<AdminTeamLinkDto | null>(null);
    readonly showDeleteConfirm = signal(false);
    readonly deleteTarget = signal<AdminTeamLinkDto | null>(null);

    ngOnInit(): void {
        this.loadLinks();
        this.loadAvailableTeams();
    }

    loadLinks(): void {
        this.isLoading.set(true);
        this.errorMessage.set(null);
        this.service.list().subscribe({
            next: data => {
                this.links.set(data);
                this.isLoading.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to load team links.');
                this.isLoading.set(false);
            }
        });
    }

    private loadAvailableTeams(): void {
        this.service.availableTeams().subscribe({
            next: teams => this.availableTeams.set(teams),
            error: () => { /* dropdown will be empty; user-visible failure shown on links load */ }
        });
    }

    openAdd(): void {
        this.showAddModal.set(true);
    }

    openEdit(link: AdminTeamLinkDto): void {
        this.editTarget.set(link);
        this.showEditModal.set(true);
    }

    onFormSaved(result: TeamLinkFormResult): void {
        if (result.mode === 'add' && result.addRequest) {
            this.service.create(result.addRequest).subscribe({
                next: () => {
                    this.toast.show('Team link added.', 'success');
                    this.showAddModal.set(false);
                    this.loadLinks();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to add team link.', 'danger', 4000);
                }
            });
        } else if (result.mode === 'edit' && result.updateRequest && result.docId) {
            this.service.update(result.docId, result.updateRequest).subscribe({
                next: () => {
                    this.toast.show('Team link updated.', 'success');
                    this.showEditModal.set(false);
                    this.editTarget.set(null);
                    this.loadLinks();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to update team link.', 'danger', 4000);
                }
            });
        }
    }

    confirmDelete(link: AdminTeamLinkDto): void {
        this.deleteTarget.set(link);
        this.showDeleteConfirm.set(true);
    }

    onDeleteConfirmed(): void {
        const target = this.deleteTarget();
        if (!target) return;
        this.showDeleteConfirm.set(false);
        this.service.delete(target.docId).subscribe({
            next: () => {
                this.toast.show('Team link removed.', 'success');
                this.deleteTarget.set(null);
                this.loadLinks();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to delete team link.', 'danger', 4000);
            }
        });
    }
}
