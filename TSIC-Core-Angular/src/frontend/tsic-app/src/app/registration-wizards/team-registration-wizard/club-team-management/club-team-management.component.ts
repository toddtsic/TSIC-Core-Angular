import { Component, input, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';

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
    imports: [CommonModule],
    templateUrl: './club-team-management.component.html',
    styleUrls: ['./club-team-management.component.scss']
})
export class ClubTeamManagementComponent implements OnInit {
    clubName = input.required<string>();

    private readonly http = inject(HttpClient);

    teams = signal<ClubTeamDto[]>([]);
    isLoading = signal<boolean>(false);
    errorMessage = signal<string | null>(null);

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
                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Error loading club teams:', err);
                this.errorMessage.set(err.error?.message || 'Failed to load club teams');
                this.isLoading.set(false);
            }
        });
    }
}
