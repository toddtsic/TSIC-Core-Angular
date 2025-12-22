import { Component, EventEmitter, OnInit, Output, inject, input, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { FormFieldDataService } from '../../../core/services/form-field-data.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import { ClubTeamAddModalComponent } from '../club-team-add-modal/club-team-add-modal.component';

interface ClubTeamDto {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string;
    isActive: boolean;
    hasBeenUsed: boolean;
}

@Component({
    selector: 'app-club-team-management',
    standalone: true,
    imports: [CommonModule, FormsModule, ClubTeamAddModalComponent],
    templateUrl: './club-team-management.component.html',
    styleUrls: ['./club-team-management.component.scss']
})
export class ClubTeamManagementComponent implements OnInit {
    clubName = input.required<string>();

    @Output() teamsLoaded = new EventEmitter<number>();
    @Output() addTeam = new EventEmitter<void>();

    private readonly http = inject(HttpClient);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly teamRegService = inject(TeamRegistrationService);

    teams = signal<ClubTeamDto[]>([]);
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);
    addTeamModal = viewChild<ClubTeamAddModalComponent>('addTeamModal');

    gradYearOptions = computed(() => this.fieldData.getOptionsForDataSource('gradYears'));
    levelOfPlayOptions = computed(() => this.fieldData.getOptionsForDataSource('List_Lops'));

    ngOnInit(): void {
        this.loadTeams();
    }

    loadTeams(): void {
        this.isLoading.set(true);
        this.errorMessage.set(null);

        const url = `${environment.apiUrl}/team-registration/club-library-teams`;

        this.http.get<ClubTeamDto[]>(url).subscribe({
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
}
