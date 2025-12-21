import { Component, OnInit, inject, input, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ClubTeamManagementService, ClubTeamManagementDto, ClubTeamOperationResponse } from '../services/club-team-management.service';
import { TeamRegistrationService } from '../services/team-registration.service';

@Component({
  selector: 'app-club-team-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './club-team-management.component.html',
  styleUrls: ['./club-team-management.component.scss']
})
export class ClubTeamManagementComponent implements OnInit {
  clubName = input.required<string>();
  
  // Data
  teams = signal<ClubTeamManagementDto[]>([]);
  
  // UI state
  isLoading = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  showAddTeamModal = signal<boolean>(false);
  isSubmitting = signal<boolean>(false);
  
  // Add team form
  newTeamName = signal<string>('');
  newTeamGradYear = signal<string>('');
  newTeamLOP = signal<string>('');
  
  // Delete confirmation
  teamToDelete = signal<ClubTeamManagementDto | null>(null);
  showDeleteConfirm = signal<boolean>(false);

  private readonly managementService = inject(ClubTeamManagementService);
  private readonly registrationService = inject(TeamRegistrationService);

  ngOnInit(): void {
    this.loadTeams();
  }

  loadTeams(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    
    this.managementService.getClubTeamsForManagement(this.clubName()).subscribe({
      next: (teams) => {
        this.teams.set(teams);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading teams:', err);
        this.errorMessage.set('Failed to load club teams. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  openAddTeamModal(): void {
    this.newTeamName.set('');
    this.newTeamGradYear.set('');
    this.newTeamLOP.set('');
    this.errorMessage.set(null);
    this.showAddTeamModal.set(true);
  }

  closeAddTeamModal(): void {
    this.showAddTeamModal.set(false);
  }

  addTeam(): void {
    const name = this.newTeamName().trim();
    const gradYear = this.newTeamGradYear().trim();
    const lop = this.newTeamLOP().trim();

    if (!name || !gradYear || !lop) {
      this.errorMessage.set('Please fill in all required fields');
      return;
    }

    this.isSubmitting.set(true);
    
    this.registrationService.addNewClubTeam({
      clubTeamName: name,
      clubTeamGradYear: gradYear,
      clubTeamLevelOfPlay: lop
    }).subscribe({
      next: () => {
        this.successMessage.set('Team added successfully!');
        this.closeAddTeamModal();
        setTimeout(() => this.successMessage.set(null), 3000);
        this.loadTeams();
        this.isSubmitting.set(false);
      },
      error: (err) => {
        console.error('Error adding team:', err);
        this.errorMessage.set(err.error?.message || 'Failed to add team. Please try again.');
        this.isSubmitting.set(false);
      }
    });
  }

  toggleTeamStatus(team: ClubTeamManagementDto): void {
    if (team.isActive) {
      this.inactivateTeam(team);
    } else {
      this.activateTeam(team);
    }
  }

  inactivateTeam(team: ClubTeamManagementDto): void {
    this.managementService.inactivateClubTeam(team.clubTeamId).subscribe({
      next: () => {
        this.successMessage.set(`${team.clubTeamName} inactivated successfully`);
        setTimeout(() => this.successMessage.set(null), 3000);
        this.loadTeams();
      },
      error: (err) => {
        console.error('Error inactivating team:', err);
        this.errorMessage.set(err.error?.message || 'Failed to inactivate team');
      }
    });
  }

  activateTeam(team: ClubTeamManagementDto): void {
    this.managementService.activateClubTeam(team.clubTeamId).subscribe({
      next: () => {
        this.successMessage.set(`${team.clubTeamName} activated successfully`);
        setTimeout(() => this.successMessage.set(null), 3000);
        this.loadTeams();
      },
      error: (err) => {
        console.error('Error activating team:', err);
        this.errorMessage.set(err.error?.message || 'Failed to activate team');
      }
    });
  }

  confirmDelete(team: ClubTeamManagementDto): void {
    this.teamToDelete.set(team);
    this.showDeleteConfirm.set(true);
  }

  cancelDelete(): void {
    this.teamToDelete.set(null);
    this.showDeleteConfirm.set(false);
  }

  deleteTeam(): void {
    const team = this.teamToDelete();
    if (!team) return;

    this.managementService.deleteClubTeam(team.clubTeamId).subscribe({
      next: () => {
        this.successMessage.set(`${team.clubTeamName} deleted successfully`);
        setTimeout(() => this.successMessage.set(null), 3000);
        this.cancelDelete();
        this.loadTeams();
      },
      error: (err) => {
        console.error('Error deleting team:', err);
        this.errorMessage.set(err.error?.message || 'Failed to delete team');
        this.cancelDelete();
      }
    });
  }

  // Computed properties for UI
  activeTeams = computed(() => this.teams().filter(t => t.isActive));
  inactiveTeams = computed(() => this.teams().filter(t => !t.isActive));
  hasActiveTeams = computed(() => this.activeTeams().length > 0);
  hasInactiveTeams = computed(() => this.inactiveTeams().length > 0);
}
