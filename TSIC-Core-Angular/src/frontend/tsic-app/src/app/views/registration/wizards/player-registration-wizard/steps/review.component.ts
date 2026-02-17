import { TeamService } from '../team.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

import { RegistrationWizardService } from '../registration-wizard.service';
import { PaymentService } from '../services/payment.service';
import { WaiverStateService } from '../services/waiver-state.service';
import { JobService } from '@infrastructure/services/job.service';

@Component({
  selector: 'app-rw-review',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './review.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReviewComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  private readonly jobService = inject(JobService);
  public readonly state = inject(RegistrationWizardService);
  public readonly teamService = inject(TeamService);
  public readonly paySvc = inject(PaymentService);
  public readonly waiverState = inject(WaiverStateService);

  selectedPlayers() {
    return this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => {
        const fp = this.state.familyPlayers().find(f => f.playerId === p.playerId);
        return {
          userId: p.playerId,
          name: `${p.firstName} ${p.lastName}`.trim(),
          dob: fp?.dob || null,
          gender: fp?.gender || null
        };
      });
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

  getAmountForPlayer(playerId: string): number | null {
    const li = this.paySvc.lineItems().find(i => i.playerId === playerId);
    return li ? li.amount : null;
  }

  jobName(): string {
    return this.jobService.getCurrentJob()?.jobName || this.state.jobPath() || '';
  }

  isMultiTeamMode(): boolean {
    const t = (this.state.teamConstraintType() || '').toUpperCase();
    return !t || t === 'BYCLUBNAME';
  }

  hasWaivers(): boolean {
    return this.waiverState.waiverDefinitions().length > 0;
  }

  waiverSummary(): { title: string; accepted: boolean }[] {
    return this.waiverState.waiverDefinitions().map(d => ({
      title: d.title,
      accepted: this.waiverState.isWaiverAccepted(d.id)
    }));
  }
}
