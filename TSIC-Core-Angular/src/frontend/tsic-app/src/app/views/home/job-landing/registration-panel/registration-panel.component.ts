import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { isTournament } from '@infrastructure/constants/job-type.constants';
import { SelfRosterUpdateModalService } from '@views/registration/self-roster-update/self-roster-update-modal.service';
import { SmartMarkerComponent } from '@widgets/communications/smart-bulletins/smart-marker.component';

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
	imports: [RouterLink, SmartMarkerComponent],
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
	private readonly tournament = computed(() => isTournament(this.jobService.currentJob()?.jobTypeId));

	// Tournament reframes player registration as "self-rostering" (the team is the
	// registering entity; a player joins one) — centralized so it never drifts.
	protected readonly title = computed(() => this.tournament() ? 'Player & Coach Self-Rostering' : 'Registration');

	// ── Self-Rostering section — one link per open self-registration role ────────
	// Each gated on phase (allowedKeys) AND its pulse flag.
	protected readonly selfRosterLinks = computed<RegLink[]>(() => {
		const p = this.pulse();
		if (!p || this.registered()) return [];
		const allowed = this.allowedKeys();
		const base = this.base();
		const t = this.tournament();
		const links: RegLink[] = [];
		if (allowed.has('register-player') && p.playerRegistrationOpen) {
			links.push({ key: 'player', label: t ? 'Self-Roster a Player' : 'Register a Player',
				icon: 'bi-person-plus', routerLink: `${base}/registration/player` });
		}
		if (allowed.has('register-team') && p.teamRegistrationOpen) {
			links.push({ key: 'team', label: 'Register a Team',
				icon: 'bi-people', routerLink: `${base}/registration/team` });
		}
		if (allowed.has('register-coach') && p.staffRegistrationOpen) {
			links.push({ key: 'coach', label: t ? 'Self-Roster a Coach' : 'Register a Coach',
				icon: 'bi-person-badge', routerLink: `${base}/registration/adult`, queryParams: { role: 'coach' } });
		}
		if (allowed.has('register-referee') && p.refereeRegistrationOpen) {
			links.push({ key: 'referee', label: 'Register a Referee',
				icon: 'bi-whistle', routerLink: `${base}/registration/adult`, queryParams: { role: 'referee' } });
		}
		if (allowed.has('register-recruiter') && p.recruiterRegistrationOpen) {
			links.push({ key: 'recruiter', label: 'Register a College Recruiter',
				icon: 'bi-mortarboard', routerLink: `${base}/registration/adult`, queryParams: { role: 'recruiter' } });
		}
		return links;
	});

	// The self-roster-update (change team / uniform # / cancel) is meaningful while
	// player registration is open — that's when self-service fixes save support calls.
	private readonly showChange = computed(() =>
		this.allowedKeys().has('register-player') && !!this.pulse()?.playerRegistrationOpen);

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
			items.push({ key: 'self-roster-update', icon: 'bi-pencil-square', variant: 'feature',
				label: 'Change Team or Uniform #', sublabel: '…or cancel a player registration',
				action: 'self-roster-update' });
		}

		if (registered && isPF) {
			if (allowed.has('my-registration')) {
				items.push({ key: 'my-registration', icon: 'bi-person-vcard', label: 'My Registration',
					routerLink: `${base}/registration/player`, queryParams: { step: 'players' } });
			}
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
			if (allowed.has('my-teams')) {
				items.push({ key: 'my-teams', icon: 'bi-people', label: 'My Teams',
					routerLink: `${base}/registration/team`, queryParams: { step: 'teams' } });
			}
			if ((p.myClubRepTotalOwed ?? 0) > 0) {
				items.push({ key: 'clubrep-balance', icon: 'bi-cash-stack', variant: 'alert',
					label: 'Final Balance Due', sublabel: this.money(p.myClubRepTotalOwed!),
					routerLink: `${base}/registration/team`, queryParams: { step: 'payment' } });
			}
			if (allowed.has('team-insurance') && p.offerTeamRegsaverInsurance && p.myClubRepHasTeamWithoutRegsaver === true) {
				items.push({ key: 'team-insurance', icon: 'bi-shield-check', label: 'Add Team RegSaver',
					sublabel: 'Forgot it at checkout?', routerLink: `${base}/ClubRepVIUpdate` });
			}
		}

		return items;
	});

	protected readonly showManage = computed(() => this.manageItems().length > 0);

	// NB: Public Rosters is NOT a section here anymore — it's a first-class card in the
	// Smart Bulletins band (so it can't orphan when this panel doesn't render).

	/** The panel self-hides when no section has content. */
	protected readonly hasContent = computed(() =>
		this.selfRosterLinks().length > 0 || this.showManage());

	openSelfRosterUpdate(): void {
		this.sruModal.open(this.jobPath());
	}

	private money(v: number): string {
		return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(v);
	}
}
