import { Component, computed, inject, signal, effect, input, output } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { TeamRegistrationService } from '../services/team-registration.service';

interface ClubTeamDto {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
}

@Component({
    selector: 'app-club-team-add-modal',
    standalone: true,
    imports: [FormsModule],
    templateUrl: './club-team-add-modal.component.html',
    styleUrls: ['./club-team-add-modal.component.scss']
})
export class ClubTeamAddModalComponent {
    // Signal input (Angular 21)
    clubName = input.required<string>();

    // Signal output (Angular 21)
    teamAdded = output<void>();

    private readonly http = inject(HttpClient);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly teamService = inject(TeamRegistrationService);

    visible = signal<boolean>(false);
    teams = signal<ClubTeamDto[]>([]);
    errorMessage = signal<string | null>(null);
    guidelinesCollapsed = true;
    existingTeamsCollapsed = true;
    stayOpenOnSubmit = false;

    gradYearOptions = signal<(string | number)[]>(this.buildGradYears());
    levelOfPlayOptions = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

    constructor() {
        // Load teams whenever modal becomes visible
        effect(() => {
            if (this.visible()) {
                this.loadTeams();
            }
        });
    }

    open(): void {
        this.visible.set(true);
    }

    close(): void {
        this.visible.set(false);
        this.stayOpenOnSubmit = false;
        this.errorMessage.set(null);
    }

    private loadTeams(): void {
        const url = `${environment.apiUrl}/team-registration/club-library-teams`;

        this.http.get<ClubTeamDto[]>(url).subscribe({
            next: (teams) => {
                this.teams.set(teams);
            },
            error: (err) => {
                console.error('Error loading club teams:', err);
                this.errorMessage.set(err.error?.message || 'Failed to load club teams');
            }
        });
    }

    addNewClubTeam(teamData: { clubTeamName: string; clubTeamGradYear: string | number; clubTeamLevelOfPlay: string }, formRef?: any): void {
        this.errorMessage.set(null);

        if (formRef?.invalid) {
            formRef.form?.markAllAsTouched();
            return;
        }

        const normalizedGradYear = teamData.clubTeamGradYear === 'N/A' ? null : teamData.clubTeamGradYear;

        // Strip descriptive text from level of play, keep only the number
        const lopMatch = /^(\d+)/.exec(teamData.clubTeamLevelOfPlay);
        const normalizedLevelOfPlay = lopMatch ? lopMatch[1] : teamData.clubTeamLevelOfPlay;

        this.teamService.addNewClubTeam({
            clubTeamName: teamData.clubTeamName,
            clubTeamGradYear: normalizedGradYear as any,
            clubTeamLevelOfPlay: normalizedLevelOfPlay
        }).subscribe({
            next: () => {
                const shouldStayOpen = this.stayOpenOnSubmit;
                this.stayOpenOnSubmit = false;

                // Reload teams to show the newly added team
                this.loadTeams();

                // Notify parent component to refresh its table
                this.teamAdded.emit();

                if (shouldStayOpen) {
                    // Reset the form but keep modal open
                    if (formRef) {
                        formRef.resetForm();
                    }
                    // Force existing teams accordion open to show updated list
                    this.existingTeamsCollapsed = false;
                } else {
                    this.close();
                }
            },
            error: (err) => {
                console.error('Failed to add club team:', err);
                this.errorMessage.set(err.error?.message || 'Failed to add team. Please try again.');
            }
        });
    }

    private buildGradYears(): (string | number)[] {
        const startYear = new Date().getFullYear();
        const span = 10; // current year through current year + 9
        const rollingYears = Array.from({ length: span }, (_, i) => startYear + i);
        return [...rollingYears, 'N/A'];
    }
}
