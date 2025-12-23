import { Component, EventEmitter, OnInit, Output, inject, input, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FormFieldDataService } from '../../../core/services/form-field-data.service';
import { UserPreferencesService } from '../../../core/services/user-preferences.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { ClubTeamAddModalComponent } from '../club-team-add-modal/club-team-add-modal.component';
import { TeamEditModalComponent } from '../team-edit-modal/team-edit-modal.component';
import { ClubTeamManagementDto } from '../../../core/api/models';

@Component({
    selector: 'app-club-team-management',
    standalone: true,
    imports: [CommonModule, FormsModule, ClubTeamAddModalComponent, TeamEditModalComponent],
    templateUrl: './club-team-management.component.html',
    styleUrls: ['./club-team-management.component.scss']
})
export class ClubTeamManagementComponent implements OnInit {
    clubName = input.required<string>();

    @Output() teamsLoaded = new EventEmitter<number>();
    @Output() addTeam = new EventEmitter<void>();

    private readonly fieldData = inject(FormFieldDataService);
    private readonly userPrefs = inject(UserPreferencesService);
    private readonly teamRegService = inject(TeamRegistrationService);

    teams = signal<ClubTeamManagementDto[]>([]);
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    activeTab = signal<'active' | 'inactive'>('active');
    infoExpanded = signal<boolean>(!this.userPrefs.isTeamLibraryInfoRead());
    infoAlreadyRead = signal<boolean>(this.userPrefs.isTeamLibraryInfoRead());

    searchTerm = signal<string>('');
    collapsedYears = signal<Set<string>>(new Set());

    addTeamModal = viewChild<ClubTeamAddModalComponent>('addTeamModal');
    editTeamModal = viewChild<TeamEditModalComponent>('editTeamModal');

    // Computed signals for filtered teams
    activeTeams = computed(() => this.teams().filter(t => t.isActive));
    inactiveTeams = computed(() => this.teams().filter(t => !t.isActive));
    filteredTeams = computed(() => {
        const teams = this.activeTab() === 'active' ? this.activeTeams() : this.inactiveTeams();
        const search = this.searchTerm().toLowerCase().trim();
        if (!search) return teams;
        return teams.filter(t =>
            t.clubTeamName?.toLowerCase().includes(search) ||
            t.clubTeamGradYear?.toString().includes(search)
        );
    });

    // Group filtered teams by graduation year
    groupedTeams = computed(() => {
        const teams = this.filteredTeams();
        const groups = new Map<string, ClubTeamManagementDto[]>();

        teams.forEach(team => {
            const year = String(team.clubTeamGradYear);
            if (!groups.has(year)) {
                groups.set(year, []);
            }
            groups.get(year)!.push(team);
        });

        // Convert to array and sort by year ascending
        return Array.from(groups.entries())
            .sort((a, b) => parseInt(a[0], 10) - parseInt(b[0], 10))
            .map(([year, teams]) => ({ year, teams }));
    });

    activeCount = computed(() => this.activeTeams().length);
    inactiveCount = computed(() => this.inactiveTeams().length);

    gradYearOptions = computed(() => this.fieldData.getOptionsForDataSource('gradYears'));
    levelOfPlayOptions = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

    ngOnInit(): void {
        this.loadTeams();
    }

    loadTeams(): void {
        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.teamRegService.getClubTeams().subscribe({
            next: (teams) => {
                this.teams.set(teams);
                this.teamsLoaded.emit(teams.length);
                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Error loading club teams:', err);
                this.errorMessage.set(err.error?.message || 'Failed to load club teams');
                this.teamsLoaded.emit(0);
                this.isLoading.set(false);
            }
        });
    }

    requestAddTeam(): void {
        this.addTeamModal()?.open();
    }

    switchTab(tab: 'active' | 'inactive'): void {
        this.activeTab.set(tab);
    }

    modifyTeam(team: ClubTeamManagementDto): void {
        this.editTeamModal()?.open(team, (updatedTeam) => {
            // Update the team in the local array
            const index = this.teams().findIndex(t => t.clubTeamId === updatedTeam.clubTeamId);
            if (index !== -1) {
                const updated = [...this.teams()];
                updated[index] = updatedTeam;
                this.teams.set(updated);
            }
        });
    }

    toggleInfo(): void {
        this.infoExpanded.set(!this.infoExpanded());
    }

    acknowledgeInfo(): void {
        this.userPrefs.markTeamLibraryInfoAsRead();
        this.infoAlreadyRead.set(true);
        this.infoExpanded.set(false);
    }

    toggleYearCollapse(year: string): void {
        const collapsed = new Set(this.collapsedYears());
        if (collapsed.has(year)) {
            collapsed.delete(year);
        } else {
            collapsed.add(year);
        }
        this.collapsedYears.set(collapsed);
    }

    isYearCollapsed(year: string): boolean {
        return this.collapsedYears().has(year);
    }

    expandAll(): void {
        this.collapsedYears.set(new Set());
    }

    collapseAll(): void {
        const allYears = new Set(this.groupedTeams().map(g => g.year));
        this.collapsedYears.set(allYears);
    }
}
