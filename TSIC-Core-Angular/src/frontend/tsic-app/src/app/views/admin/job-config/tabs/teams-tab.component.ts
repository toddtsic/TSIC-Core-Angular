import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import type { UpdateJobConfigTeamsRequest } from '@core/api';

@Component({
  selector: 'app-teams-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './teams-tab.component.html',
})
export class TeamsTabComponent {
  protected readonly svc = inject(JobConfigService);

  bRegistrationAllowTeam = signal<boolean | null>(null);
  regformNameTeam = signal('');
  regformNameClubRep = signal('');
  bClubRepAllowEdit = signal<boolean | null>(null);
  bClubRepAllowDelete = signal<boolean | null>(null);
  bClubRepAllowAdd = signal<boolean | null>(null);
  bRestrictPlayerTeamsToAgerange = signal<boolean | null>(null);
  bTeamPushDirectors = signal<boolean | null>(null);
  bUseWaitlists = signal(false);
  bShowTeamNameOnlyInSchedules = signal(false);

  // SuperUser-only
  bOfferTeamRegsaverInsurance = signal<boolean | null>(null);

  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const t = this.svc.teams();
      if (!t) return;
      this.bRegistrationAllowTeam.set(t.bRegistrationAllowTeam);
      this.regformNameTeam.set(t.regformNameTeam);
      this.regformNameClubRep.set(t.regformNameClubRep);
      this.bClubRepAllowEdit.set(t.bClubRepAllowEdit);
      this.bClubRepAllowDelete.set(t.bClubRepAllowDelete);
      this.bClubRepAllowAdd.set(t.bClubRepAllowAdd);
      this.bRestrictPlayerTeamsToAgerange.set(t.bRestrictPlayerTeamsToAgerange);
      this.bTeamPushDirectors.set(t.bTeamPushDirectors);
      this.bUseWaitlists.set(t.bUseWaitlists);
      this.bShowTeamNameOnlyInSchedules.set(t.bShowTeamNameOnlyInSchedules);
      this.bOfferTeamRegsaverInsurance.set(t.bOfferTeamRegsaverInsurance ?? null);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
      this.svc.saveHandler.set(() => this.save());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
      this.svc.markClean('teams');
    } else {
      this.svc.markDirty('teams');
    }
  }

  save(): void {
    this.svc.saveTeams(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigTeamsRequest {
    const req: UpdateJobConfigTeamsRequest = {
      bRegistrationAllowTeam: this.bRegistrationAllowTeam(),
      regformNameTeam: this.regformNameTeam(),
      regformNameClubRep: this.regformNameClubRep(),
      bClubRepAllowEdit: this.bClubRepAllowEdit(),
      bClubRepAllowDelete: this.bClubRepAllowDelete(),
      bClubRepAllowAdd: this.bClubRepAllowAdd(),
      bRestrictPlayerTeamsToAgerange: this.bRestrictPlayerTeamsToAgerange(),
      bTeamPushDirectors: this.bTeamPushDirectors(),
      bUseWaitlists: this.bUseWaitlists(),
      bShowTeamNameOnlyInSchedules: this.bShowTeamNameOnlyInSchedules(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferTeamRegsaverInsurance = this.bOfferTeamRegsaverInsurance();
    }
    return req;
  }
}
