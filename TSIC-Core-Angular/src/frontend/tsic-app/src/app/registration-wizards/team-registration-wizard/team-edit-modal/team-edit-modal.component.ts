import { Component, Input, computed, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FormFieldDataService } from '../../../core/services/form-field-data.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { ClubTeamManagementDto, UpdateClubTeamRequest } from '../../../core/api/models';

@Component({
    selector: 'app-team-edit-modal',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './team-edit-modal.component.html',
    styleUrls: ['./team-edit-modal.component.scss']
})
export class TeamEditModalComponent {
    private readonly fieldData = inject(FormFieldDataService);
    private readonly teamService = inject(TeamRegistrationService);

    visible = signal<boolean>(false);
    team = signal<ClubTeamManagementDto | null>(null);
    isSubmitting = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // Form fields
    teamName = signal<string>('');
    gradYear = signal<string>('');
    levelOfPlay = signal<string>('');

    // Computed flags
    hasBeenRegistered = computed(() => this.team()?.hasBeenRegisteredForAnyEvent ?? false);
    isActive = computed(() => this.team()?.isActive ?? true);
    canEditName = computed(() => !this.hasBeenRegistered());
    canEditGradYear = computed(() => !this.hasBeenRegistered());

    // Options
    gradYearOptions = signal<(string | number)[]>(this.buildGradYears());
    levelOfPlayOptions = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

    // Callback for successful operations
    private onSuccessCallback?: (team: ClubTeamManagementDto) => void;

    constructor() {
        // Reset form when modal becomes visible
        effect(() => {
            if (this.visible() && this.team()) {
                const t = this.team()!;
                this.teamName.set(t.clubTeamName);
                this.gradYear.set(t.clubTeamGradYear);

                // Find matching dropdown option for level of play
                // Database stores "5", dropdown has "5 (strongest)"
                const lopOptions = this.levelOfPlayOptions();
                const matchingOption = lopOptions.find(opt => opt.value.startsWith(t.clubTeamLevelOfPlay));
                this.levelOfPlay.set(matchingOption?.value || t.clubTeamLevelOfPlay);

                this.errorMessage.set(null);
                this.successMessage.set(null);
            }
        });
    }

    open(team: ClubTeamManagementDto, onSuccess?: (team: ClubTeamManagementDto) => void): void {
        this.team.set(team);
        this.onSuccessCallback = onSuccess;
        this.visible.set(true);
    }

    close(): void {
        this.visible.set(false);
        this.team.set(null);
        this.isSubmitting.set(false);
        this.errorMessage.set(null);
        this.successMessage.set(null);
        this.onSuccessCallback = undefined;
    }

    saveChanges(): void {
        const currentTeam = this.team();
        if (!currentTeam || this.isSubmitting()) {
            return;
        }

        // Validate team name
        const trimmedName = this.teamName().trim();
        if (!trimmedName) {
            this.errorMessage.set('Team name is required');
            return;
        }
        if (trimmedName.length > 80) {
            this.errorMessage.set('Team name cannot exceed 80 characters');
            return;
        }

        // Validate level of play
        if (this.levelOfPlay().length > 50) {
            this.errorMessage.set('Level of play cannot exceed 50 characters');
            return;
        }

        this.isSubmitting.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        // Strip descriptive text from level of play, keep only the number
        const lopMatch = this.levelOfPlay().match(/^(\d+)/);
        const normalizedLevelOfPlay = lopMatch ? lopMatch[1] : this.levelOfPlay();

        const request: UpdateClubTeamRequest = {
            clubTeamId: currentTeam.clubTeamId,
            clubTeamName: trimmedName,
            clubTeamGradYear: this.gradYear(),
            clubTeamLevelOfPlay: normalizedLevelOfPlay
        };

        this.teamService.updateClubTeam(
            request,
            (response) => {
                this.isSubmitting.set(false);
                this.successMessage.set(response.message || 'Team updated successfully');

                // Update local team object
                const updatedTeam: ClubTeamManagementDto = {
                    ...currentTeam,
                    clubTeamName: request.clubTeamName,
                    clubTeamGradYear: request.clubTeamGradYear,
                    clubTeamLevelOfPlay: request.clubTeamLevelOfPlay
                };
                this.team.set(updatedTeam);

                // Call success callback
                if (this.onSuccessCallback) {
                    this.onSuccessCallback(updatedTeam);
                }

                // Close modal after brief delay to show success message
                setTimeout(() => this.close(), 1500);
            },
            (error) => {
                this.isSubmitting.set(false);
                this.errorMessage.set(error);
            }
        );
    }

    toggleActive(): void {
        const currentTeam = this.team();
        if (!currentTeam || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        const operation = currentTeam.isActive
            ? this.teamService.inactivateClubTeam.bind(this.teamService)
            : this.teamService.activateClubTeam.bind(this.teamService);

        operation(
            currentTeam.clubTeamId,
            (response) => {
                this.isSubmitting.set(false);
                this.successMessage.set(response.message || 'Team status updated');

                // Update local team object
                const updatedTeam: ClubTeamManagementDto = {
                    ...currentTeam,
                    isActive: !currentTeam.isActive
                };
                this.team.set(updatedTeam);

                // Call success callback
                if (this.onSuccessCallback) {
                    this.onSuccessCallback(updatedTeam);
                }

                // Close modal after brief delay
                setTimeout(() => this.close(), 1500);
            },
            (error) => {
                this.isSubmitting.set(false);
                this.errorMessage.set(error);
            }
        );
    }

    // Delete confirmation state
    confirmingDelete = signal<boolean>(false);
    confirmText = signal<string>('');
    confirmDisabled = computed(() => {
        if (this.isSubmitting()) return true;
        // Require typing DELETE for permanent deletion (no registration history)
        if (!this.hasBeenRegistered()) {
            return this.confirmText().trim().toUpperCase() !== 'DELETE';
        }
        return false;
    });

    beginDelete(): void {
        this.errorMessage.set(null);
        this.successMessage.set(null);
        this.confirmingDelete.set(true);
    }

    cancelDelete(): void {
        this.confirmText.set('');
        this.confirmingDelete.set(false);
    }

    confirmDelete(): void {
        const currentTeam = this.team();
        if (!currentTeam || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        this.teamService.deleteClubTeam(
            currentTeam.clubTeamId,
            (response) => {
                this.isSubmitting.set(false);
                this.successMessage.set(response.message || (currentTeam.hasBeenRegisteredForAnyEvent ? 'Team moved to inactive' : 'Team deleted successfully'));

                // Update local team object if soft-deleted
                if (currentTeam.hasBeenRegisteredForAnyEvent) {
                    const updatedTeam: ClubTeamManagementDto = {
                        ...currentTeam,
                        isActive: false
                    };
                    this.team.set(updatedTeam);
                }

                // Call success callback
                if (this.onSuccessCallback) {
                    this.onSuccessCallback({
                        ...currentTeam,
                        isActive: false
                    });
                }

                // Reset confirm state and close modal after brief delay
                this.cancelDelete();
                setTimeout(() => this.close(), 1500);
            },
            (error) => {
                this.isSubmitting.set(false);
                this.errorMessage.set(error);
            }
        );
    }

    private buildGradYears(): (string | number)[] {
        const currentYear = new Date().getFullYear();
        const years: number[] = [];
        for (let i = 0; i < 20; i++) {
            years.push(currentYear + i);
        }
        return years;
    }
}
