import { Component, OnInit, OnDestroy, inject, signal, computed, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription, of, catchError, switchMap } from 'rxjs';
import { AddClubFormComponent } from '../add-club-form/add-club-form.component';
import { TeamRegistrationService } from '../services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ClubRepClubDto } from '@core/api';

/**
 * Club management modal for viewing, adding, editing, and removing clubs.
 * Used within team registration wizard to manage club rep's clubs.
 */
@Component({
    selector: 'app-club-management-modal',
    standalone: true,
    imports: [CommonModule, FormsModule, AddClubFormComponent],
    templateUrl: './club-management-modal.component.html',
    styleUrls: ['./club-management-modal.component.scss']
})
export class ClubManagementModalComponent implements OnInit, OnDestroy {
    // Services
    private readonly teamRegService = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);

    // Outputs
    readonly clubsChanged = output<void>();
    readonly closed = output<void>();

    // State
    readonly clubs = signal<ClubRepClubDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);
    readonly showAddClubForm = signal(false);
    readonly clubToEdit = signal<ClubRepClubDto | null>(null);
    readonly editClubName = signal('');
    readonly isEditingSubmitting = signal(false);
    readonly clubToDelete = signal<ClubRepClubDto | null>(null);
    readonly deleteConfirmText = signal('');
    readonly isDeletingSubmitting = signal(false);

    // Computed
    readonly deleteConfirmValid = computed(() =>
        this.deleteConfirmText().trim().toUpperCase() === 'DELETE'
    );

    private loadClubsSubscription?: Subscription;
    private editClubSubscription?: Subscription;
    private deleteClubSubscription?: Subscription;

    ngOnInit(): void {
        this.loadClubs();
    }

    ngOnDestroy(): void {
        this.loadClubsSubscription?.unsubscribe();
        this.editClubSubscription?.unsubscribe();
        this.deleteClubSubscription?.unsubscribe();
    }

    loadClubs(): void {
        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.loadClubsSubscription?.unsubscribe();

        this.loadClubsSubscription = this.teamRegService.getMyClubs().subscribe({
            next: (clubs) => {
                this.clubs.set(clubs);
                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Error loading clubs:', err);
                this.errorMessage.set('Failed to load clubs. Please try again.');
                this.isLoading.set(false);
            }
        });
    }

    // ============================================================
    // ADD CLUB HANDLERS
    // ============================================================

    openAddClubForm(): void {
        this.showAddClubForm.set(true);
        this.errorMessage.set(null);
    }

    closeAddClubForm(): void {
        this.showAddClubForm.set(false);
    }

    handleClubAdded(): void {
        this.showAddClubForm.set(false);
        // Cancel any active edit/delete state before reload
        this.cancelEdit();
        this.cancelDelete();
        this.loadClubs();
        this.clubsChanged.emit();
        this.toast.show('Club added successfully', 'success', 3000);
    }

    // ============================================================
    // EDIT CLUB HANDLERS
    // ============================================================

    startEdit(club: ClubRepClubDto): void {
        if (club.isInUse) {
            this.toast.show(
                'Cannot edit club name - teams have been registered under this club',
                'warning',
                5000
            );
            return;
        }

        // Cancel any previous edit before starting new one
        if (this.clubToEdit() && this.clubToEdit()?.clubName !== club.clubName) {
            this.cancelEdit();
        }

        this.errorMessage.set(null);
        this.clubToEdit.set(club);
        this.editClubName.set(club.clubName);
    }

    cancelEdit(): void {
        this.clubToEdit.set(null);
        this.editClubName.set('');
    }

    submitEdit(): void {
        const club = this.clubToEdit();
        const newName = this.editClubName().trim();

        if (!club || !newName) {
            this.toast.show('Club name is required', 'danger', 3000);
            return;
        }

        if (newName === club.clubName) {
            this.cancelEdit();
            return;
        }

        if (newName.length > 200) {
            this.toast.show('Club name cannot exceed 200 characters', 'danger', 3000);
            return;
        }

        this.isEditingSubmitting.set(true);

        this.editClubSubscription?.unsubscribe();

        this.editClubSubscription = this.teamRegService
            .updateClubName(club.clubName, newName)
            .pipe(
                switchMap(() => this.teamRegService.getMyClubs()),
                catchError((err) => {
                    const message =
                        err.error?.Message ||
                        err.error?.message ||
                        'Failed to update club name';
                    this.toast.show(message, 'danger', 5000);
                    this.isEditingSubmitting.set(false);
                    console.error('Edit club error:', err);
                    return of(null);
                })
            )
            .subscribe({
                next: (clubs) => {
                    if (clubs) {
                        this.clubs.set(clubs);
                        this.toast.show('Club name updated successfully', 'success', 3000);
                        this.cancelEdit();
                        this.clubsChanged.emit();
                    }
                    this.isEditingSubmitting.set(false);
                },
                error: (err) => {
                    console.error('Unexpected error in submitEdit:', err);
                    this.toast.show('An unexpected error occurred. Please try again.', 'danger', 5000);
                    this.isEditingSubmitting.set(false);
                }
            });
    }

    // ============================================================
    // DELETE CLUB HANDLERS
    // ============================================================

    startDelete(club: ClubRepClubDto): void {
        if (club.isInUse) {
            this.toast.show(
                'Cannot delete club - teams have been registered under this club',
                'warning',
                5000
            );
            return;
        }

        // Cancel any previous delete before starting new one
        if (this.clubToDelete()) {
            this.cancelDelete();
        }

        this.clubToDelete.set(club);
        this.deleteConfirmText.set('');
    }

    cancelDelete(): void {
        this.clubToDelete.set(null);
        this.deleteConfirmText.set('');
    }

    confirmDelete(): void {
        const club = this.clubToDelete();

        if (!club) return;

        if (!this.deleteConfirmValid()) {
            this.toast.show('Please type DELETE to confirm', 'warning', 3000);
            return;
        }

        this.isDeletingSubmitting.set(true);

        this.deleteClubSubscription?.unsubscribe();

        this.deleteClubSubscription = this.teamRegService
            .removeClubFromRep(club.clubName)
            .pipe(
                switchMap(() => this.teamRegService.getMyClubs()),
                catchError((err) => {
                    const message =
                        err.error?.Message ||
                        err.error?.message ||
                        'Failed to delete club';
                    this.toast.show(message, 'danger', 5000);
                    this.isDeletingSubmitting.set(false);
                    console.error('Delete club error:', err);
                    return of(null);
                })
            )
            .subscribe({
                next: (clubs) => {
                    if (clubs) {
                        this.clubs.set(clubs);
                        this.toast.show('Club removed successfully', 'success', 3000);
                        this.cancelDelete();
                        this.clubsChanged.emit();
                    }
                    this.isDeletingSubmitting.set(false);
                },
                error: (err) => {
                    console.error('Unexpected error in confirmDelete:', err);
                    this.toast.show('An unexpected error occurred. Please try again.', 'danger', 5000);
                    this.isDeletingSubmitting.set(false);
                }
            });
    }

    // ============================================================
    // MODAL CONTROLS
    // ============================================================

    close(): void {
        // Reset all state for clean modal on next open
        this.showAddClubForm.set(false);
        this.errorMessage.set(null);
        this.cancelEdit();
        this.cancelDelete();
        this.closed.emit();
    }
}
