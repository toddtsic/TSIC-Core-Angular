import { Component, OnInit, signal, computed, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamRegistrationService } from '../services/team-registration.service';
import type { ClubTeamDto, RegisteredTeamDto, AgeGroupDto } from '../../../core/api/models';
import { JobContextService } from '../../../core/services/job-context.service';
import { FormFieldDataService } from '../../../core/services/form-field-data.service';

/**
 * Teams Step Component
 * 
 * Displays dual-table UI for managing club team registrations:
 * - Left: Available ClubTeams (global, persistent across events)
 * - Right: Registered Teams (event-specific, linked to current Job)
 * 
 * Features:
 * - Search/filter teams by name, grade year, level of play
 * - Click to register ClubTeam for event (creates Teams record)
 * - Click to unregister Team from event (deletes Teams record if unpaid)
 * - Display financial details (FeeBase, FeeProcessing, FeeTotal, PaidTotal, OwedTotal)
 * - Age group availability indicators
 * - Add new ClubTeam modal
 */
@Component({
    selector: 'app-teams-step',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './teams-step.component.html',
    styleUrls: ['./teams-step.component.scss']
})
export class TeamsStepComponent implements OnInit {
    // Injected services
    private readonly teamService = inject(TeamRegistrationService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);

    // Dropdown options from JsonOptions
    availableGradYears = computed(() => this.fieldData.getOptionsForDataSource('gradYears'));
    availableLevelsOfPlay = computed(() => this.fieldData.getOptionsForDataSource('levelOfPlay'));

    // Search/filter state
    searchTerm = signal<string>('');
    filterGradeYear = signal<string>('');
    filterLevelOfPlay = signal<string>('');

    // Data signals (populated from service)
    availableClubTeams = signal<ClubTeamDto[]>([]);
    registeredTeams = signal<RegisteredTeamDto[]>([]);
    ageGroups = signal<AgeGroupDto[]>([]);

    // Club metadata (clubName from parent wizard, clubId from API)
    clubName = input.required<string>();
    clubId = signal<number | null>(null);

    // UI state
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    showAddTeamModal = signal<boolean>(false);

    // Financial summary
    totalOwed = computed(() => {
        return this.registeredTeams().reduce((sum, team) => sum + (team.owedTotal || 0), 0);
    });

    // Filtered available teams
    filteredAvailableTeams = computed(() => {
        let teams = this.availableClubTeams();
        const search = this.searchTerm().toLowerCase();
        const gradeYear = this.filterGradeYear();
        const levelOfPlay = this.filterLevelOfPlay();

        if (search) {
            teams = teams.filter(t => t.clubTeamName.toLowerCase().includes(search));
        }
        if (gradeYear) {
            teams = teams.filter(t => t.clubTeamGradYear === gradeYear);
        }
        if (levelOfPlay) {
            teams = teams.filter(t => t.clubTeamLevelOfPlay === levelOfPlay);
        }

        return teams;
    });

    // Filtered registered teams
    filteredRegisteredTeams = computed(() => {
        let teams = this.registeredTeams();
        const search = this.searchTerm().toLowerCase();
        const gradeYear = this.filterGradeYear();
        const levelOfPlay = this.filterLevelOfPlay();

        if (search) {
            teams = teams.filter(t => t.clubTeamName.toLowerCase().includes(search));
        }
        if (gradeYear) {
            teams = teams.filter(t => t.clubTeamGradYear === gradeYear);
        }
        if (levelOfPlay) {
            teams = teams.filter(t => t.clubTeamLevelOfPlay === levelOfPlay);
        }

        return teams;
    });

    // Get unique grade years from all teams
    gradeYears = computed(() => {
        const years = new Set<string>();
        for (const t of this.availableClubTeams()) {
            years.add(t.clubTeamGradYear);
        }
        for (const t of this.registeredTeams()) {
            years.add(t.clubTeamGradYear);
        }
        return Array.from(years).sort((a, b) => a.localeCompare(b));
    });

    // Get unique levels of play from all teams
    levelsOfPlay = computed(() => {
        const levels = new Set<string>();
        for (const t of this.availableClubTeams()) {
            levels.add(t.clubTeamLevelOfPlay);
        }
        for (const t of this.registeredTeams()) {
            levels.add(t.clubTeamLevelOfPlay);
        }
        return Array.from(levels).sort((a, b) => a.localeCompare(b));
    });

    ngOnInit(): void {
        this.loadTeamsMetadata();
    }

    /**
     * Load teams metadata from backend
     * This will populate:
     * - clubId, clubName
     * - availableClubTeams (ClubTeams not yet registered for this event)
     * - registeredTeams (Teams already registered for this event)
     * - ageGroups (with availability info)
     */
    private loadTeamsMetadata(): void {
        const jobPath = this.jobContext.jobPath();
        const clubName = this.clubName();

        if (!jobPath) {
            this.errorMessage.set('Event not found. Please navigate from a valid event link.');
            return;
        }

        if (!clubName) {
            this.errorMessage.set('Club name is required.');
            return;
        }

        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.teamService.getTeamsMetadata(jobPath, clubName).subscribe({
            next: (response) => {
                this.clubId.set(response.clubId);
                this.availableClubTeams.set(response.availableClubTeams);
                this.registeredTeams.set(response.registeredTeams);
                this.ageGroups.set(response.ageGroups);
                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Failed to load teams metadata:', err);
                this.errorMessage.set(err.error?.message || 'Failed to load team data. Please try again.');
                this.isLoading.set(false);
            }
        });
    }

    /**
     * Register a ClubTeam for this event
     * Creates a Teams record linked to the ClubTeam and current Job
     */
    registerTeam(clubTeam: ClubTeamDto): void {
        const jobPath = this.jobContext.jobPath();
        if (!jobPath) {
            this.errorMessage.set('Event not found');
            return;
        }

        this.errorMessage.set(null);

        this.teamService.registerTeamForEvent({
            clubTeamId: clubTeam.clubTeamId,
            jobPath: jobPath,
            ageGroupId: undefined // Auto-determine from team grad year
        }).subscribe({
            next: () => {
                // Refresh metadata to get updated state
                this.loadTeamsMetadata();
            },
            error: (err) => {
                console.error('Failed to register team:', err);
                this.errorMessage.set(err.error?.message || 'Failed to register team. Please try again.');
            }
        });
    }

    /**
     * Unregister a Team from this event
     * Deletes the Teams record if it has no payments
     */
    unregisterTeam(team: RegisteredTeamDto): void {
        if (team.paidTotal > 0) {
            this.errorMessage.set('Cannot unregister a team that has payments. Please contact support.');
            return;
        }

        this.errorMessage.set(null);

        this.teamService.unregisterTeamFromEvent(team.teamId).subscribe({
            next: () => {
                // Refresh metadata to get updated state
                this.loadTeamsMetadata();
            },
            error: (err) => {
                console.error('Failed to unregister team:', err);
                this.errorMessage.set(err.error?.message || 'Failed to unregister team. Please try again.');
            }
        });
    }

    /**
     * Open the add new team modal
     */
    openAddTeamModal(): void {
        this.showAddTeamModal.set(true);
    }

    /**
     * Close the add new team modal
     */
    closeAddTeamModal(): void {
        this.showAddTeamModal.set(false);
    }

    /**
     * Add a new ClubTeam to the club
     */
    addNewClubTeam(teamData: NewClubTeamData): void {
        this.errorMessage.set(null);

        this.teamService.addNewClubTeam(teamData).subscribe({
            next: () => {
                this.closeAddTeamModal();
                // Refresh metadata to get the new team
                this.loadTeamsMetadata();
            },
            error: (err) => {
                console.error('Failed to add club team:', err);
                this.errorMessage.set(err.error?.message || 'Failed to add team. Please try again.');
            }
        });
    }

    /**
     * Get age group availability info by name
     */
    getAgeGroupAvailability(ageGroupName: string): AgeGroupDto | undefined {
        return this.ageGroups().find(ag => ag.ageGroupName === ageGroupName);
    }

    /**
     * Check if an age group is full
     */
    isAgeGroupFull(ageGroupName: string): boolean {
        const ag = this.getAgeGroupAvailability(ageGroupName);
        return ag ? ag.registeredCount >= ag.maxTeams : false;
    }

    /**
     * Clear all filters
     */
    clearFilters(): void {
        this.searchTerm.set('');
        this.filterGradeYear.set('');
        this.filterLevelOfPlay.set('');
    }
}

interface NewClubTeamData {
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
}
