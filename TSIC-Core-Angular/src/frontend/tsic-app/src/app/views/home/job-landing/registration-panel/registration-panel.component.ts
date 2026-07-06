import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { isTournament, isLeague } from '@infrastructure/constants/job-type.constants';
import { derivePhase, isPlayerRegistrationEffectivelyOpen } from '@shared/landing/landing-phase';
import { SelfRosterUpdateModalService } from '@views/registration/self-roster-update/self-roster-update-modal.service';

interface RegLink {
	key: string;
	label: string;
	icon: string;
	routerLink: string;
	queryParams?: Record<string, string>;
}

interface ManageItem {
	key: string;
	label: string;
	sublabel?: string;
	icon: string;
	/** undefined = plain row; 'feature' = filled primary; 'alert' = balance-due emphasis. */
	variant?: 'feature' | 'alert';
	routerLink?: string;
	queryParams?: Record<string, string>;
	/** When set the row is a button that opens the modal instead of navigating. */
	action?: 'self-roster-update';
}

/**
 * Registration compound viewer — the self-assembling capture of the hand-authored
 * registration bulletins ("Player & Coach Registration and Waiver Wizard", and the
 * typical player-reg page's "forgot insurance at checkout" / "final balance" notes).
 * CONDITIONALLY constructs its sections from the live pulse + viewer state:
 *
 *   • Self-Rostering — links for each self-registration role the director has open
 *   • Manage — the self-service hub that kills support calls: the FINAL BALANCE DUE
 *     (deposit/balance jobs), self-roster-update (change team / uniform # / cancel),
 *     My Registration, and the "forgot insurance at checkout?" RegSaver add-ons
 *     (player + team). Player and club-rep dimensions both handled.
 *   • Rosters — the public roster view
 *
 * Each section/row is gated on phase (allowedKeys) AND its pulse flag. The whole
 * panel self-hides when no section has content.
 */
@Component({
	selector: 'app-registration-panel',
	standalone: true,
	imports: [RouterLink],
	templateUrl: './registration-panel.component.html',
	styleUrl: './registration-panel.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationPanelComponent {
	/** Absolute, jobPath-prefixed base (the jobPath is in the path, so it's preserved). */
	readonly jobPath = input.required<string>();

	/** The CTA keys the current lifecycle phase allows (the parent's CTAS_BY_PHASE
	 *  set). Each section is gated on BOTH this AND its pulse flag, so a stale toggle
	 *  on a concluded event can't resurrect a self-roster link. */
	readonly allowedKeys = input<ReadonlySet<string>>(new Set<string>());

	/** Public-preview mode: render the panel as an anonymous visitor would see it,
	 *  ignoring the session user's regId/role and the my* pulse overlay. Set true for
	 *  Director/SuperDirector/SuperUser previewing the Smart Bulletins band so the
	 *  dashboard shows exactly what the public sees. Default false = personalized. */
	readonly publicView = input(false);

	private readonly pulseService = inject(JobPulseService);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);
	private readonly sruModal = inject(SelfRosterUpdateModalService);

	private readonly pulse = computed(() => this.pulseService.pulse());
	private readonly base = computed(() => `/${this.jobPath()}`);

	// A viewer holding a regId is past the "register" stage (Player/Family sees
	// "My Registration" rather than a self-roster link). Anonymous still registers.
	private readonly registered = computed(() => !this.publicView() && !!this.auth.currentUser()?.regId);
	private readonly isPlayerOrFamily = computed(() => {
		if (this.publicView()) return false;
		const r = this.auth.currentUser()?.role;
		return r === Roles.Player || r === Roles.Family;
	});
	// Acting as a club rep — suppresses the PLAYER self-roster-update link (that's a
	// player/family self-service flow; a club rep manages rosters via "Edit My Rosters").
	// Ignored in public-preview (an admin previews the anonymous public view).
	private readonly isClubRep = computed(() =>
		!this.publicView() && this.auth.currentUser()?.role === Roles.ClubRep);
	// A logged-in ADULT registrant (Staff = a self-rostered coach) — holds a regId but
	// isn't a player/family or club rep. The player self-service flows (self-roster-update,
	// "Change Team or Uniform #") don't apply; they get "My Registration" into the adult
	// wizard instead, mirroring the header-bar task menu's Staff branch.
	private readonly isRegisteredAdult = computed(() =>
		this.registered() && !this.isPlayerOrFamily() && !this.isClubRep());
	private readonly tournament = computed(() => isTournament(this.jobService.currentJob()?.jobTypeId));
	private readonly league = computed(() => isLeague(this.jobService.currentJob()?.jobTypeId));
	// The competitive settings — tournament OR league — where referees officiate and
	// college recruiters scout. Those two registration roles are gated to it.
	private readonly competitive = computed(() => this.tournament() || this.league());
	// Concluded = post-event (schedule released + last game passed). Derived from the
	// SHARED phase resolver (same pulse, same phase as the band) so the two can't drift.
	private readonly concluded = computed(() => derivePhase(this.pulse(), new Date()) === 'concluded');

	// Panel title — phase-aware: "Registration Links" while the event is live, but a
	// concluded event isn't taking registrations, so it reads as a post-event hub
	// ("Wrap-Up") for the rows that survive: outstanding balances, rosters, teams.
	protected readonly title = computed(() => this.concluded() ? 'Wrap-Up' : 'Registration Links');

	// ── Self-Rostering section — one link per open self-registration role ────────
	// Each gated on phase (allowedKeys) AND its pulse flag.
	protected readonly selfRosterLinks = computed<RegLink[]>(() => {
		const p = this.pulse();
		if (!p) return [];
		const allowed = this.allowedKeys();
		const base = this.base();

		// A registered player/family is past the "register" stage, so this personal
		// column shows "My Registration" (moved here from Manage) instead of the
		// self-roster register links — same gates the Manage row carried.
		if (this.registered()) {
			if (this.isPlayerOrFamily() && allowed.has('my-registration')) {
				return [{ key: 'my-registration', label: 'My Registration', icon: 'bi-person-vcard',
					routerLink: `${base}/registration/player`, queryParams: { step: 'players' } }];
			}
			// Staff (self-rostered coach) — My Registration routes into the ADULT wizard.
			// The adult wizard REQUIRES ?role=<key> or it shows an "incomplete link" error,
			// so the coach roleKey rides the URL (same wiring as the header-bar Staff branch).
			if (this.isRegisteredAdult() && allowed.has('my-registration')) {
				return [{ key: 'my-registration-adult', label: 'My Registration', icon: 'bi-person-vcard',
					routerLink: `${base}/registration/adult`, queryParams: { role: 'coach', step: 'profile' } }];
			}
			return [];
		}

		const t = this.tournament();
		const comp = this.competitive();
		const links: RegLink[] = [];
		// isPlayerRegistrationEffectivelyOpen = the canonical predicate shared with derivePhase,
		// the player wizard's `registrationClosed` gate, and the invite guard: the job toggle being
		// on isn't enough — at least one team must be within its registration-availability window
		// (Teams.Effectiveasofdate..Expireondate), else the click dead-ends on the wizard's
		// "registration closed" panel. A showcase whose team windows have all passed must NOT show this.
		if (allowed.has('register-player') && isPlayerRegistrationEffectivelyOpen(p)) {
			links.push({ key: 'player', label: t ? 'Self-Roster Player' : 'Register Player',
				icon: 'bi-person-plus', routerLink: `${base}/registration/player` });
		}
		if (allowed.has('register-coach') && p.staffRegistrationOpen) {
			links.push({ key: 'coach', label: 'Register Coach',
				icon: 'bi-person-badge', routerLink: `${base}/registration/adult`, queryParams: { role: 'coach' } });
		}
		if (comp && allowed.has('register-referee') && p.refereeRegistrationOpen) {
			links.push({ key: 'referee', label: 'Register Referee',
				icon: 'bi-flag', routerLink: `${base}/registration/adult`, queryParams: { role: 'referee' } });
		}
		if (comp && allowed.has('register-recruiter') && p.recruiterRegistrationOpen) {
			links.push({ key: 'recruiter', label: 'Register College Recruiter',
				icon: 'bi-mortarboard', routerLink: `${base}/registration/adult`, queryParams: { role: 'recruiter' } });
		}
		return links;
	});

	// Section header tracks WHO this column serves: "Player" when only the player
	// self-roster (or My Registration) is present, "Adult" for coach/referee/recruiter
	// only, "Player/Adult" when both classes are open. Derived from the links already
	// computed above, so it can never disagree with what's rendered.
	protected readonly selfRosterTitle = computed(() => {
		const links = this.selfRosterLinks();
		const hasPlayer = links.some(l => l.key === 'player' || l.key === 'my-registration');
		const hasAdult = links.some(l => l.key === 'coach' || l.key === 'referee' || l.key === 'recruiter' || l.key === 'my-registration-adult');
		return hasPlayer && hasAdult ? 'Player/Adult' : hasAdult ? 'Adult' : 'Player';
	});

	// The self-roster-update (change team / uniform # / cancel) is meaningful while
	// player registration is open — that's when self-service fixes save support calls.
	// PLAYER/family flow only: a club rep manages rosters via "Edit My Rosters", so this
	// link is suppressed for the club-rep role (still shown to anonymous/players). TOURNAMENT
	// ONLY: self-rostering (join/change team, change uniform #) is a tournament concept —
	// leagues/clubs/camps don't self-roster onto teams, so the link never appears there.
	private readonly showChange = computed(() =>
		this.tournament()
		&& this.allowedKeys().has('register-player')
		&& isPlayerRegistrationEffectivelyOpen(this.pulse())
		&& !this.isClubRep()
		&& !this.isRegisteredAdult());

	// ── Manage section — the support-call-killing self-service hub ───────────────
	// Order is deliberate: money owed first (most urgent), then the change/cancel
	// fix, then My Registration, then the "forgot insurance" add-ons. Player and
	// club-rep (teams) dimensions both handled.
	protected readonly manageItems = computed<ManageItem[]>(() => {
		const p = this.pulse();
		if (!p) return [];
		const allowed = this.allowedKeys();
		const base = this.base();
		const registered = this.registered();
		const isPF = this.isPlayerOrFamily();
		const items: ManageItem[] = [];

		// ── Player ──
		if (registered && isPF) {
			// Final balance — deposit-aware wording: a full-payment job has no deposit
			// phase, so there's no "final" balance, just an amount due.
			if (allowed.has('pay-balance') && (p.myRegistrationOwedTotal ?? 0) > 0) {
				const deposit = !this.jobService.currentJob()?.bPlayersFullPaymentRequired;
				items.push({ key: 'player-balance', icon: 'bi-cash-stack', variant: 'alert',
					label: deposit ? 'Final Balance Due' : 'Balance Due',
					sublabel: this.money(p.myRegistrationOwedTotal!),
					routerLink: `${base}/registration/player`, queryParams: { step: 'payment' } });
			}
		}

		// Change team / uniform # — or cancel (the self-roster-update modal).
		if (this.showChange()) {
			items.push({ key: 'self-roster-update', icon: 'bi-pencil-square',
				label: 'Change Team or Uniform #', sublabel: '…or cancel a player registration',
				action: 'self-roster-update' });
		}

		if (registered && isPF) {
			// My Registration moved to the Player/Adult column (selfRosterLinks).
			// Forgot insurance at checkout — the RegSaver add-on flow.
			if (allowed.has('player-insurance') && p.offerPlayerRegsaverInsurance && p.myHasPurchasedPlayerRegsaver !== true) {
				items.push({ key: 'player-insurance', icon: 'bi-shield-check', label: 'Add RegSaver Insurance',
					sublabel: 'Forgot it at checkout?', routerLink: `${base}/PlayerVIUpdate` });
			}
		}

		// ── Club rep (teams) — myClubRepTeamCount is only populated for a club rep
		// scoped to this job, so > 0 encodes both role and has-teams. Suppressed in
		// public-preview (it's the previewing admin's own overlay, not public data). ──
		if (!this.publicView() && (p.myClubRepTeamCount ?? 0) > 0) {
			// "My Teams" now leads the Teams column (moved out of Manage, per Ann) —
			// see showMyTeams() below.
			// Tournament-only club-rep roster editor — the migrated legacy ClubTeamRosters
			// (change uniform #, move/remove players across the rep's own teams). Club-rep
			// role only (gated by this block; never anonymous/public). Phase-gated with My
			// Teams so it doesn't surface before rosters exist — and NOT once concluded (a
			// finished event can't be edited; writes are blocked by the expiry gate anyway).
			// Route is ClubRep-guarded too.
			if (this.tournament() && allowed.has('my-teams') && !this.concluded()) {
				items.push({ key: 'clubrep-rosters', icon: 'bi-card-list', label: 'Edit My Rosters',
					sublabel: 'Change uniform #s, move or remove players',
					routerLink: `${base}/rosters/club` });
			}
			if ((p.myClubRepTotalOwed ?? 0) > 0) {
				items.push({ key: 'clubrep-balance', icon: 'bi-cash-stack', variant: 'alert',
					label: 'Final Balance Due', sublabel: this.money(p.myClubRepTotalOwed!),
					routerLink: `${base}/registration/team`, queryParams: { step: 'payment' } });
			}
			if (this.competitive() && allowed.has('team-insurance') && p.offerTeamRegsaverInsurance && p.myClubRepHasTeamWithoutRegsaver === true) {
				items.push({ key: 'team-insurance', icon: 'bi-shield-check', label: 'Add Team RegSaver',
					sublabel: 'Forgot it at checkout?', routerLink: `${base}/ClubRepVIUpdate` });
			}
		}

		return items;
	});

	protected readonly showManage = computed(() => this.manageItems().length > 0);

	// ── Teams section — Register a Team (top), then the public roster view ───────
	// Register a Team is a fresh-registration action (hidden once registered); it
	// leads the column. Public Rosters is job-level (shows in public-preview too).
	// Both gated on phase (allowedKeys) AND their pulse flag.
	// My Teams — the club rep's existing teams, LEADING the Teams column (moved here from
	// Manage, per Ann). Club-rep role only: myClubRepTeamCount > 0 encodes role + has-teams;
	// suppressed in public-preview (the previewing admin's own overlay, not public data).
	protected readonly showMyTeams = computed(() => {
		const p = this.pulse();
		if (!p || this.publicView()) return false;
		return this.allowedKeys().has('my-teams') && (p.myClubRepTeamCount ?? 0) > 0;
	});
	protected readonly myTeamsLink = computed(() => `${this.base()}/registration/team`);

	// Leagues don't register teams (players self-roster onto pre-built league teams) —
	// so the Register Team link never shows there, even if a stale teamRegistrationOpen
	// flag lingers in the pulse. Tournaments/clubs still show it, pulse-permitting.
	protected readonly showRegisterTeam = computed(() => {
		const p = this.pulse();
		if (!p || this.registered() || this.league()) return false;
		return this.allowedKeys().has('register-team') && p.teamRegistrationOpen;
	});
	protected readonly teamRegLink = computed(() => `${this.base()}/registration/team`);

	// Public Rosters — TOURNAMENT only (same rationale as the Change Team link: public
	// self-rosters are a tournament concept). Leagues/clubs/camps don't surface them here.
	protected readonly showRosters = computed(() => {
		const p = this.pulse();
		return this.tournament() && !!p?.publicRostersAvailable && this.allowedKeys().has('rosters');
	});
	protected readonly rostersLink = computed(() => `${this.base()}/rosters/public`);

	/** Whether the Teams column has anything to show. */
	protected readonly showTeams = computed(() => this.showMyTeams() || this.showRegisterTeam() || this.showRosters());

	/** The panel self-hides when no section has content. */
	protected readonly hasContent = computed(() =>
		this.selfRosterLinks().length > 0 || this.showManage() || this.showTeams());

	openSelfRosterUpdate(): void {
		this.sruModal.open(this.jobPath());
	}

	private money(v: number): string {
		return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(v);
	}
}
