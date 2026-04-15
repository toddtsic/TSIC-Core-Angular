import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
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
export class TeamsTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  bRegistrationAllowTeam = linkedSignal(() => this.svc.teams()?.bRegistrationAllowTeam ?? null);
  bTeamRegRequiresToken = linkedSignal(() => this.svc.teams()?.bTeamRegRequiresToken ?? false);
  bClubRepAllowEdit = linkedSignal(() => this.svc.teams()?.bClubRepAllowEdit ?? null);
  bClubRepAllowDelete = linkedSignal(() => this.svc.teams()?.bClubRepAllowDelete ?? null);
  bClubRepAllowAdd = linkedSignal(() => this.svc.teams()?.bClubRepAllowAdd ?? null);
  bRestrictPlayerTeamsToAgerange = linkedSignal(() => this.svc.teams()?.bRestrictPlayerTeamsToAgerange ?? null);
  bTeamPushDirectors = linkedSignal(() => this.svc.teams()?.bTeamPushDirectors ?? null);
  bUseWaitlists = linkedSignal(() => this.svc.teams()?.bUseWaitlists ?? false);
  bShowTeamNameOnlyInSchedules = linkedSignal(() => this.svc.teams()?.bShowTeamNameOnlyInSchedules ?? false);
  bAllowRosterViewAdult = linkedSignal(() => this.svc.teams()?.bAllowRosterViewAdult ?? false);
  bAllowRosterViewPlayer = linkedSignal(() => this.svc.teams()?.bAllowRosterViewPlayer ?? false);

  // SuperUser-only
  bOfferTeamRegsaverInsurance = linkedSignal(() => this.svc.teams()?.bOfferTeamRegsaverInsurance ?? null);

  private readonly cleanSnapshot = computed(() => {
    const t = this.svc.teams();
    if (!t) return '';
    const req: UpdateJobConfigTeamsRequest = {
      bRegistrationAllowTeam: t.bRegistrationAllowTeam,
      bTeamRegRequiresToken: t.bTeamRegRequiresToken,
      regformNameTeam: t.regformNameTeam ?? '',
      regformNameClubRep: t.regformNameClubRep ?? '',
      bClubRepAllowEdit: t.bClubRepAllowEdit,
      bClubRepAllowDelete: t.bClubRepAllowDelete,
      bClubRepAllowAdd: t.bClubRepAllowAdd,
      bRestrictPlayerTeamsToAgerange: t.bRestrictPlayerTeamsToAgerange,
      bTeamPushDirectors: t.bTeamPushDirectors,
      bUseWaitlists: t.bUseWaitlists,
      bShowTeamNameOnlyInSchedules: t.bShowTeamNameOnlyInSchedules,
      bAllowRosterViewAdult: t.bAllowRosterViewAdult,
      bAllowRosterViewPlayer: t.bAllowRosterViewPlayer,
    };
    if (this.svc.isSuperUser()) {
      req.bOfferTeamRegsaverInsurance = t.bOfferTeamRegsaverInsurance ?? null;
    }
    return JSON.stringify(req);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
      this.svc.markClean('teams');
    } else {
      this.svc.markDirty('teams');
    }
  }

  save(): void {
    this.svc.saveTeams(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigTeamsRequest {
    const t = this.svc.teams();
    const req: UpdateJobConfigTeamsRequest = {
      bRegistrationAllowTeam: this.bRegistrationAllowTeam(),
      bTeamRegRequiresToken: this.bTeamRegRequiresToken(),
      regformNameTeam: t?.regformNameTeam ?? '',
      regformNameClubRep: t?.regformNameClubRep ?? '',
      bClubRepAllowEdit: this.bClubRepAllowEdit(),
      bClubRepAllowDelete: this.bClubRepAllowDelete(),
      bClubRepAllowAdd: this.bClubRepAllowAdd(),
      bRestrictPlayerTeamsToAgerange: this.bRestrictPlayerTeamsToAgerange(),
      bTeamPushDirectors: this.bTeamPushDirectors(),
      bUseWaitlists: this.bUseWaitlists(),
      bShowTeamNameOnlyInSchedules: this.bShowTeamNameOnlyInSchedules(),
      bAllowRosterViewAdult: this.bAllowRosterViewAdult(),
      bAllowRosterViewPlayer: this.bAllowRosterViewPlayer(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferTeamRegsaverInsurance = this.bOfferTeamRegsaverInsurance();
    }
    return req;
  }
}
