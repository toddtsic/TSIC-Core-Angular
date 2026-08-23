import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { JobConfigService } from '../job-config.service';
import { JOB_CONFIG_RTE_HEIGHT } from '../shared/rte-config';
import { TsicRteDirective } from '@shared-ui/rte.directive';
import { RegistrationReadinessComponent } from '../components/registration-readiness.component';
import type { UpdateJobConfigTeamsRequest } from '@core/api';

@Component({
  selector: 'app-teams-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, RichTextEditorAllModule, TsicRteDirective, RegistrationReadinessComponent, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './teams-tab.component.html',
})
export class TeamsTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

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
  benableStp = linkedSignal(() => this.svc.teams()?.benableStp ?? false);

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
      benableStp: t.benableStp,
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

  // ── Roster-visibility informed consent (ruling 2026-08-14) ──
  // Checking either roster toggle is a PII disclosure decision, and on Tournament/League
  // jobs coaches self-place onto teams with NO director vetting — so this toggle is the
  // only thing between a self-registered adult and minors' data. It must never flip on
  // casually: checking it opens a confirm dialog that states the exposure and pins the
  // responsibility on the director; Cancel reverts to off. Unchecking (reducing exposure)
  // never prompts. Job-clone resets both flags to false (JobCloneResetRules), so every
  // new season re-extracts this consent.
  readonly pendingRosterConsent = signal<'player' | 'adult' | null>(null);

  readonly rosterConsentTitle = computed(() =>
    this.pendingRosterConsent() === 'player'
      ? 'Expose team rosters to players?'
      : 'Expose team rosters to coaches & staff?');

  readonly rosterConsentMessage = computed(() => {
    const who = this.pendingRosterConsent() === 'player'
      ? 'every rostered player (and their family account)'
      : 'every adult registered as staff on a team — including coaches who self-registered onto that team';
    return `<p>Turning this on lets <strong>${who}</strong> view that team's full roster` +
      ` — including minors' names and the personal details your registration form collects.</p>` +
      `<ul>` +
      `<li>Confirm this disclosure is permitted by <strong>your organization's privacy policy</strong>` +
      ` and the youth-data laws that apply to you.</li>` +
      `<li>TSIC does not make this decision — <strong>you, the event director, are responsible for it</strong>.</li>` +
      `</ul>` +
      this.superUserCaveat();
  });

  // ── SuperUser caveat, shared by every consent dialog on this tab ──
  // A SuperUser can edit any job's settings, which means we can hand out a customer's
  // data on their behalf without them ever knowing a dialog appeared. These are the
  // director's disclosures to authorise, not ours; when a SuperUser is the one clicking,
  // the dialog says so out loud rather than quietly accepting the click.
  // Uses the global .tsic-callout grammar (styles/_callouts.scss) rather than a local
  // class: this string is injected through the dialog's [innerHTML], and Angular's
  // emulated encapsulation does not stamp _ngcontent onto dynamically-set HTML, so a
  // component-scoped rule would silently not apply.
  private readonly superUserCaveat = computed(() => this.svc.isSuperUser()
    ? `<div class="tsic-callout tsic-callout--warning tsic-callout--block mt-3">` +
      `<i class="bi bi-exclamation-triangle" aria-hidden="true"></i>` +
      `<span><strong>You are a SuperUser — this is not your decision to make.</strong>` +
      ` This setting belongs to the event director. Turn it on only if the director asked` +
      ` you to, and note who asked. If you are enabling it to test, or to unblock yourself,` +
      ` stop and ask them instead.</span>` +
      `</div>`
    : '');

  // ── Stay-to-Play third-party disclosure (2026-08-23) ──
  // Same informed-consent shape as the roster toggles above, and for a sharper reason:
  // this one sends club-rep contact data OUT to a company TSIC does not control. The
  // flag is what opens the STPAdmin vendor login (it gates the role offer at sign-in and
  // the /api/stp read), so checking it is the moment the data starts flowing. Was a
  // SuperUser-only checkbox on the Mobile/Store tab until 2026-08-23, where the person
  // whose decision it is could not see it. Unchecking never prompts; job-clone resets it
  // to false so every new season re-extracts the consent.
  readonly pendingStpConsent = signal(false);

  readonly stpConsentMessage = computed(() =>
    `<p>Turning this on shares this event's <strong>club rep contact details</strong>` +
    ` — name, email, cell phone, zip — and each club's team counts with a` +
    ` <strong>third-party Stay-to-Play housing vendor</strong>.</p>` +
    `<ul>` +
    `<li>The vendor gets their own login to this event and can export the list at will.` +
    ` Once exported, <strong>that data is outside TSIC and we cannot recall it</strong>.</li>` +
    `<li>Confirm the sharing is permitted by <strong>your organization's privacy policy</strong>` +
    ` and covered by whatever agreement you have with the vendor.</li>` +
    `<li>TSIC does not make this decision — <strong>you, the event director, are responsible for it</strong>.</li>` +
    `</ul>` +
    `<p class="text-muted">Turning it back off closes the vendor's login immediately.</p>` +
    this.superUserCaveat());

  onStpChange(checked: boolean): void {
    this.benableStp.set(checked);
    if (checked) {
      this.pendingStpConsent.set(true);
      return;
    }
    this.onFieldChange();
  }

  confirmStpConsent(): void {
    this.pendingStpConsent.set(false);
    this.onFieldChange();
  }

  cancelStpConsent(): void {
    this.benableStp.set(false);
    this.pendingStpConsent.set(false);
    this.onFieldChange();
  }

  onRosterVisibilityChange(which: 'player' | 'adult', checked: boolean): void {
    const sig = which === 'player' ? this.bAllowRosterViewPlayer : this.bAllowRosterViewAdult;
    sig.set(checked);
    if (checked) {
      // Checkbox shows checked while the dialog is up (it renders what's being
      // confirmed); dirty-tracking waits for the decision.
      this.pendingRosterConsent.set(which);
      return;
    }
    this.onFieldChange();
  }

  confirmRosterConsent(): void {
    this.pendingRosterConsent.set(null);
    this.onFieldChange();
  }

  cancelRosterConsent(): void {
    const which = this.pendingRosterConsent();
    if (which === 'player') this.bAllowRosterViewPlayer.set(false);
    if (which === 'adult') this.bAllowRosterViewAdult.set(false);
    this.pendingRosterConsent.set(null);
    this.onFieldChange();
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
      benableStp: this.benableStp(),
      adultRegConfirmationEmail: this.adultRegConfirmationEmail(),
      adultRegConfirmationOnScreen: this.adultRegConfirmationOnScreen(),
    };
    if (this.svc.isSuperUser()) {
      req.bOfferTeamRegsaverInsurance = this.bOfferTeamRegsaverInsurance();
    }
    return req;
  }
}
