import { TeamService } from '../team.service';
import { Component, EventEmitter, Output, inject } from '@angular/core';

import { RegistrationWizardService } from '../registration-wizard.service';
import { JobService } from '@infrastructure/services/job.service';

@Component({
  selector: 'app-rw-review',
  standalone: true,
  imports: [],
  templateUrl: './review.component.html'
})
export class ReviewComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  private readonly jobService = inject(JobService);
  public readonly state = inject(RegistrationWizardService);
  public readonly teamService = inject(TeamService);
  
  constructor() { }


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
