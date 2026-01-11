import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TeamRegistrationService } from '../services/team-registration.service';
import type { SuggestedTeamNameDto, RegisteredTeamDto, AgeGroupDto } from '@core/api';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import { TeamRegistrationModalComponent } from './modals/team-registration-modal/team-registration-modal.component';

// Helper to safely convert number | string to number
function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

/**
 * Teams Step Component
 * 
 * Inline team registration with intelligent suggestions from history.
 * No persistent ClubTeams library - TeamName is the cross-event identifier.
 * 
 * Features:
 * - Inline form: team name (with autocomplete), age group, level of play
 * - Suggested team names from club's historical registrations (sorted by frequency)
 * - Register team with TeamName directly (no ClubTeamId)
 * - Unregister team from event (deletes Teams record if unpaid)
 * - Display financial details and registered teams list
 */
@Component({
    selector: 'app-teams-step',
    standalone: true,
    imports: [CommonModule, FormsModule, TeamRegistrationModalComponent],
    templateUrl: './teams-step.component.html',
    styleUrls: ['./teams-step.component.scss']
})
export class TeamsStepComponent implements OnInit {
    // Injected services
    private readonly teamService = inject(TeamRegistrationService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly route = inject(ActivatedRoute);
    private readonly toast = inject(ToastService);

    // Dropdown options from JsonOptions
    availableGradYears = computed(() => this.fieldData.getOptionsForDataSource('gradYears'));
    availableLevelsOfPlay = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

    // Modal component options
    levelsOfPlayOptions = computed(() =>
        this.availableLevelsOfPlay().map(opt => ({ value: opt.value, label: opt.label }))
    );

    // Data signals (populated from service)
    suggestedTeamNames = signal<SuggestedTeamNameDto[]>([]);
    registeredTeams = signal<RegisteredTeamDto[]>([]);
    ageGroups = signal<AgeGroupDto[]>([]);

    // Track recently added team names in current session (for "Add Another" workflow)
    recentlyAddedTeamNames = signal<string[]>([]);

    // Modal state - replaces accordion
    showRegistrationModal = signal<boolean>(false);

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

    // Filtered suggested team names for modal:
    // Exclude teams already registered for this event AND recently added in this session
    suggestedTeamNamesForModal = computed(() => {
        const excludeSet = new Set(
            [
                ...this.registeredTeams().map(t => t.teamName.trim().toLowerCase()),
                ...this.recentlyAddedTeamNames().map(t => t.trim().toLowerCase())
            ]
        );
        return this.suggestedTeamNames().filter(s => !excludeSet.has(s.teamName.trim().toLowerCase()));
    });

    // UI state
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    isRegistering = signal<boolean>(false);

    // Financial summary (computed from registeredTeams signal - updates real-time)
    feesTotal = computed(() => {
        return this.registeredTeams().reduce((sum, team) => sum + toNumber(team.feeTotal), 0);
    });

    paidTotal = computed(() => {
        return this.registeredTeams().reduce((sum, team) => sum + toNumber(team.paidTotal), 0);
    });

    balanceDue = computed(() => {
        return this.registeredTeams().reduce((sum, team) => sum + toNumber(team.owedTotal), 0);
    });

    ngOnInit(): void {
        this.loadTeamsMetadata();
    }

    /**
     * Load teams metadata from backend
     * This will populate:
     * - clubId, clubName
     * - suggestedTeamNames (historical team names from this club)
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
                this.suggestedTeamNames.set(response.suggestedTeamNames);
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
     * Open registration modal
     */
    openRegistrationModal(): void {
        this.showRegistrationModal.set(true);
    }

    /**
     * Close registration modal
     */
    closeRegistrationModal(): void {
        this.showRegistrationModal.set(false);
    }

    /**
     * Handle team registration with "Add Another" option
     * Tracks the team name in session and keeps modal open for next entry
     */
    onTeamAddedAnother(data: { teamName: string; ageGroupId: string; levelOfPlay: string }): void {
        const jobPath = this.jobContext.jobPath();

        if (!jobPath) {
            this.errorMessage.set('Invalid event context');
            return;
        }

        if (this.isRegistering()) {
            return;
        }

        this.errorMessage.set(null);
        this.isRegistering.set(true);

        this.teamService.registerTeamForEvent({
            teamName: data.teamName,
            jobPath,
            ageGroupId: data.ageGroupId,
            levelOfPlay: data.levelOfPlay
        }).subscribe({
            next: () => {
                this.isRegistering.set(false);
                // Track this team name to exclude from suggestions in next iteration
                this.recentlyAddedTeamNames.update(arr => [...arr, data.teamName]);
                // Keep modal open, form will be cleared by modal component
                this.toast.show('Team registered! Add another or close to continue.', 'success');
                // Reload teams without showing loading spinner or closing modal
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
     * Handle team registration from modal
     */
    onTeamRegistered(data: { teamName: string; ageGroupId: string; levelOfPlay: string }): void {
        const jobPath = this.jobContext.jobPath();

        if (!jobPath) {
            this.errorMessage.set('Invalid event context');
            return;
        }

        if (this.isRegistering()) {
            return;
        }

        this.errorMessage.set(null);
        this.isRegistering.set(true);

        this.teamService.registerTeamForEvent({
            teamName: data.teamName,
            jobPath,
            ageGroupId: data.ageGroupId,
            levelOfPlay: data.levelOfPlay
        }).subscribe({
            next: () => {
                this.isRegistering.set(false);
                // Close modal (cleared automatically)
                this.showRegistrationModal.set(false);
                // Clear session tracking
                this.recentlyAddedTeamNames.set([]);
                // Reload teams without showing loading spinner
                this.loadTeamsMetadata(false);
                this.toast.show('Team registered successfully', 'success');
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
                this.toast.show('Team unregistered successfully', 'success');
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
}
