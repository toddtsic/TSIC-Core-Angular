import { TeamService } from '../team.service';
import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { JobService } from '../../../core/services/job.service';

@Component({
  selector: 'app-rw-review',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './review.component.html'
})
export class ReviewComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  private readonly jobService = inject(JobService);
  constructor(public state: RegistrationWizardService, public teamService: TeamService) { }


  selectedPlayers() {
    return this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
  }

  getTeamsForPlayer(playerId: string): string[] {
    const teams = this.state.selectedTeams()[playerId];
    if (!teams) return [];
    const allTeams = this.teamService.filterByEligibility(null);
    if (Array.isArray(teams)) {
      return teams.map((tid: string) => {
        const team = allTeams.find((t: any) => t.teamId === tid);
        return team?.teamName || tid;
      });
    }
    const team = allTeams.find((t: any) => t.teamId === teams);
    return [team?.teamName || teams];
  }

  jobName(): string {
    return this.jobService.getCurrentJob()?.jobName || this.state.jobPath() || '';
  }

  isMultiTeamMode(): boolean {
    // Mirror logic used elsewhere: multi team when constraint type absent or BYCLUBNAME
    const t = (this.state.teamConstraintType() || '').toUpperCase();
    return !t || t === 'BYCLUBNAME';
  }
}
