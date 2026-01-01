import { Component, OnInit, computed, inject, input, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TeamRegistrationService } from '../services/team-registration.service';
import type { ClubTeamDto, RegisteredTeamDto, AgeGroupDto } from '@core/api';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { ClubTeamAddModalComponent } from '../club-team-add-modal/club-team-add-modal.component';

// Helper to safely convert number | string to number
function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

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
    imports: [CommonModule, FormsModule, ClubTeamAddModalComponent],
    templateUrl: './teams-step.component.html',
    styleUrls: ['./teams-step.component.scss']
})
export class TeamsStepComponent implements OnInit {
    // Injected services
    private readonly teamService = inject(TeamRegistrationService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly route = inject(ActivatedRoute);

    // Dropdown options from JsonOptions
    availableGradYears = computed(() => this.fieldData.getOptionsForDataSource('gradYears'));
    availableLevelsOfPlay = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

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

    // Event display name from jobPath
    eventName = computed(() => {
        const jp = this.jobContext.jobPath();
        if (!jp) return 'This Event';
        // Convert jobPath like 'iftc-summer-2026' to 'IFTC SUMMER 2026'
        return jp.toUpperCase().replaceAll('-', ' ');
    });

    // Filtered age groups (exclude only Dropped)
    displayedAgeGroups = computed(() => {
        return this.ageGroups()
            .filter(ag => {
                const name = ag.ageGroupName.toLowerCase();
                return !name.startsWith('dropped');
            })
            .sort((a, b) => {
                // Full age groups go to bottom
                const aFull = a.registeredCount >= a.maxTeams;
                const bFull = b.registeredCount >= b.maxTeams;
                if (aFull && !bFull) return 1;
                if (!aFull && bFull) return -1;
                // Otherwise maintain original order (by name)
                return a.ageGroupName.localeCompare(b.ageGroupName);
            });
    });

    // Filtered age groups for modal (special waitlist handling)
    filteredAgeGroupsForModal = computed(() => {
        return this.ageGroups()
            .filter(ag => {
                const name = ag.ageGroupName.toLowerCase();
                // Exclude "Dropped" age groups
                if (name.startsWith('dropped')) return false;
                // Only include Waitlist if it has spots available
                if (name.startsWith('waitlist')) {
                    return (toNumber(ag.maxTeams) - toNumber(ag.registeredCount)) > 0;
                }
                return true;
            })
            .sort((a, b) => {
                const aName = a.ageGroupName.toLowerCase();
                const bName = b.ageGroupName.toLowerCase();
                const aFull = toNumber(a.registeredCount) >= toNumber(a.maxTeams) && !aName.startsWith('waitlist');
                const bFull = toNumber(b.registeredCount) >= toNumber(b.maxTeams) && !bName.startsWith('waitlist');
                const aWaitlist = aName.startsWith('waitlist');
                const bWaitlist = bName.startsWith('waitlist');

                // Available first, then full (red), then waitlist
                if (aFull && !bFull) return 1;
                if (!aFull && bFull) return -1;
                if (aWaitlist && !bWaitlist) return 1;
                if (!aWaitlist && bWaitlist) return -1;

                return a.ageGroupName.localeCompare(b.ageGroupName);
            });
    });

    // UI state
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    addTeamModal = viewChild<ClubTeamAddModalComponent>('addTeamModal');
    showAgeGroupModal = signal<boolean>(false);
    selectedClubTeamForRegistration = signal<ClubTeamDto | null>(null);
    openDropdownTeamId = signal<number | string | null>(null);
    isRegistering = signal<boolean>(false);
    selectedAgeGroupId = signal<string | null>(null);

    // Accordion collapse states
    guidelinesCollapsed = true;
    existingTeamsCollapsed = true;

    // Financial summary
    totalOwed = computed(() => {
        return this.registeredTeams().reduce((sum, team) => sum + toNumber(team.owedTotal), 0);
    });

    // Unified team view combining available and registered teams
    unifiedTeams = computed(() => {
        const available = this.availableClubTeams();
        const registered = this.registeredTeams();
        const registeredMap = new Map(registered.map(t => [t.clubTeamId, t]));

        // Combine all unique teams
        const allTeams: UnifiedTeamView[] = [];

        // Add all club teams with their registration status
        for (const clubTeam of available) {
            const registeredTeam = registeredMap.get(clubTeam.clubTeamId);
            allTeams.push({
                clubTeamId: clubTeam.clubTeamId,
                clubTeamName: clubTeam.clubTeamName,
                clubTeamGradYear: clubTeam.clubTeamGradYear,
                clubTeamLevelOfPlay: clubTeam.clubTeamLevelOfPlay,
                isRegistered: !!registeredTeam,
                registeredTeam: registeredTeam || null
            });
        }

        // Add any registered teams not in available list (shouldn't happen but defensive)
        for (const regTeam of registered) {
            if (!available.some(a => a.clubTeamId === regTeam.clubTeamId)) {
                allTeams.push({
                    clubTeamId: regTeam.clubTeamId,
                    clubTeamName: regTeam.clubTeamName,
                    clubTeamGradYear: regTeam.clubTeamGradYear,
                    clubTeamLevelOfPlay: regTeam.clubTeamLevelOfPlay,
                    isRegistered: true,
                    registeredTeam: regTeam
                });
            }
        }

        // Sort: registered first, then alphabetically by name
        return allTeams.sort((a, b) => {
            if (a.isRegistered && !b.isRegistered) return -1;
            if (!a.isRegistered && b.isRegistered) return 1;
            return a.clubTeamName.localeCompare(b.clubTeamName);
        });
    });

    // Filtered unified teams
    filteredUnifiedTeams = computed(() => {
        let teams = this.unifiedTeams();
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

    // Keep these for backward compatibility but mark as deprecated
    /** @deprecated Use unifiedTeams instead */
    filteredAvailableTeams = computed(() => {
        return this.filteredUnifiedTeams().filter(t => !t.isRegistered);
    });

    /** @deprecated Use unifiedTeams instead */
    filteredRegisteredTeams = computed(() => {
        return this.registeredTeams();
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
    private loadTeamsMetadata(showLoading: boolean = true): void {
        const jobPath = this.jobContext.resolveFromRoute(this.route);
        const clubName = this.clubName();

        if (!jobPath) {
            this.errorMessage.set('Event not found. Please navigate from a valid event link.');
            return;
        }

        if (!clubName) {
            this.errorMessage.set('Club name is required.');
            return;
        }

        if (showLoading) {
            this.isLoading.set(true);
        }
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
     * Toggle dropdown for team (used by dropdown)
     */
    toggleDropdown(team: UnifiedTeamView, event: Event): void {
        event.stopPropagation();
        const currentId = this.openDropdownTeamId();
        if (currentId === team.clubTeamId) {
            this.openDropdownTeamId.set(null);
            this.selectedClubTeamForRegistration.set(null);
        } else {
            this.openDropdownTeamId.set(team.clubTeamId);
            // Create a ClubTeamDto from the unified view
            const clubTeam: ClubTeamDto = {
                clubTeamId: team.clubTeamId,
                clubTeamName: team.clubTeamName,
                clubTeamGradYear: team.clubTeamGradYear,
                clubTeamLevelOfPlay: team.clubTeamLevelOfPlay
            };
            this.selectedClubTeamForRegistration.set(clubTeam);
        }
    }

    /**
     * Close dropdown
     */
    closeDropdown(): void {
        this.openDropdownTeamId.set(null);
    }

    /**
     * Open age group selection modal for team registration
     */
    openAgeGroupSelectionModal(clubTeam: ClubTeamDto): void {
        this.selectedClubTeamForRegistration.set(clubTeam);
        this.showAgeGroupModal.set(true);
    }

    /**
     * Close age group selection modal
     */
    closeAgeGroupModal(): void {
        this.showAgeGroupModal.set(false);
        this.selectedClubTeamForRegistration.set(null);
    }

    /**
     * Register a ClubTeam for this event with selected age group
     */
    registerTeamWithAgeGroup(ageGroupId: string, team?: ClubTeamDto | UnifiedTeamView): void {
        // Use provided team or fall back to selected team from modal
        let clubTeam: ClubTeamDto | null = null;

        if (team) {
            // Convert UnifiedTeamView to ClubTeamDto if needed
            clubTeam = {
                clubTeamId: team.clubTeamId,
                clubTeamName: team.clubTeamName,
                clubTeamGradYear: team.clubTeamGradYear,
                clubTeamLevelOfPlay: team.clubTeamLevelOfPlay
            };
        } else {
            clubTeam = this.selectedClubTeamForRegistration();
        }

        const jobPath = this.jobContext.jobPath();

        if (!clubTeam || !jobPath) {
            this.errorMessage.set('Invalid registration data');
            return;
        }

        if (this.isRegistering()) {
            return; // Prevent double-click
        }

        this.errorMessage.set(null);
        this.isRegistering.set(true);

        this.teamService.registerTeamForEvent({
            clubTeamId: clubTeam.clubTeamId,
            jobPath,
            ageGroupId,
        }).subscribe({
            next: () => {
                this.isRegistering.set(false);
                this.selectedAgeGroupId.set(null);
                this.closeAgeGroupModal();
                this.closeDropdown();

                // Reload without showing loading spinner
                this.loadTeamsMetadata(false);
            },
            error: (err) => {
                console.error('Failed to register team:', err);
                this.isRegistering.set(false);
                this.errorMessage.set(err.error?.message || 'Failed to register team. Please try again.');
            }
        });
    }

    /**
     * Unregister a Team from this event
     * Deletes the Teams record if it has no payments
     */
    unregisterTeam(team: RegisteredTeamDto): void {
        if (toNumber(team.paidTotal) > 0) {
            this.errorMessage.set('Cannot unregister a team that has payments. Please contact support.');
            return;
        }

        this.errorMessage.set(null);

        this.teamService.unregisterTeamFromEvent(team.teamId).subscribe({
            next: () => {
                // Reload without showing loading spinner
                this.loadTeamsMetadata(false);
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
        this.addTeamModal()?.open();
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

interface UnifiedTeamView {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
    isRegistered: boolean;
    registeredTeam: RegisteredTeamDto | null;
}
