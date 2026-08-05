import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import { TSIC_RTE_TOOLS } from '@shared-ui/rte-config';
import { RegistrationReadinessComponent } from '../components/registration-readiness.component';
import type { UpdateJobConfigTeamsRequest } from '@core/api';

@Component({
  selector: 'app-teams-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule, RegistrationReadinessComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './teams-tab.component.html',
})
export class TeamsTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  readonly rteTools = TSIC_RTE_TOOLS;
  readonly rteHeight = JOB_CONFIG_RTE_HEIGHT;

  bRegistrationAllowTeam = linkedSignal(() => this.svc.teams()?.bRegistrationAllowTeam ?? null);
  bTeamRegRequiresToken = linkedSignal(() => this.svc.teams()?.bTeamRegRequiresToken ?? false);
  bClubRepAllowEdit = linkedSignal(() => this.svc.teams()?.bClubRepAllowEdit ?? null);
  bClubRepAllowDelete = linkedSignal(() => this.svc.teams()?.bClubRepAllowDelete ?? null);
  bClubRepAllowAdd = linkedSignal(() => this.svc.teams()?.bClubRepAllowAdd ?? null);
  bRestrictPlayerTeamsToAgerange = linkedSignal(() => this.svc.teams()?.bRestrictPlayerTeamsToAgerange ?? null);
  bTeamPushDirectors = linkedSignal(() => this.svc.teams()?.bTeamPushDirectors ?? null);
  bAllowRosterViewAdult = linkedSignal(() => this.svc.teams()?.bAllowRosterViewAdult ?? false);
  bAllowRosterViewPlayer = linkedSignal(() => this.svc.teams()?.bAllowRosterViewPlayer ?? false);

  // Club-rep/team-registration confirmation pair (rendered by TeamRegistrationService).
  adultRegConfirmationEmail = linkedSignal(() => this.svc.teams()?.adultRegConfirmationEmail ?? null);
  adultRegConfirmationOnScreen = linkedSignal(() => this.svc.teams()?.adultRegConfirmationOnScreen ?? null);

  // Section disclosure: always starts open. AM-065 — a director must be able to prep
  // confirmation copy before releasing registration, so the editor's presence cannot
  // depend on bRegistrationAllowTeam. This was a linkedSignal seeded from that toggle;
  // because the section renders the closed-note *instead of* the editors, a job with
  // team registration off showed no text box at all until you clicked the header.
  // Plain signal, not linkedSignal, is also what removes the reseed-on-toggle path that
  // tore down the ej2 RTE mid-edit (ej2 change fires on blur, so unblurred copy was lost).
  clubRepOpen = signal(true);

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
      bAllowRosterViewAdult: t.bAllowRosterViewAdult,
      bAllowRosterViewPlayer: t.bAllowRosterViewPlayer,
      adultRegConfirmationEmail: t.adultRegConfirmationEmail,
      adultRegConfirmationOnScreen: t.adultRegConfirmationOnScreen,
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

  onRteChange(field: string, event: any): void {
    const sig = (this as any)[field];
    if (sig?.set) sig.set(event.value ?? '');
    this.onFieldChange();
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
      bAllowRosterViewAdult: this.bAllowRosterViewAdult(),
      bAllowRosterViewPlayer: this.bAllowRosterViewPlayer(),
      adultRegConfirmationEmail: this.adultRegConfirmationEmail(),
      adultRegConfirmationOnScreen: this.adultRegConfirmationOnScreen(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferTeamRegsaverInsurance = this.bOfferTeamRegsaverInsurance();
    }
    return req;
  }
}
