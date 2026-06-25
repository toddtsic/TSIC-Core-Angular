import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { isTournament } from '@infrastructure/constants/job-type.constants';
import { SelfRosterUpdateModalService } from '@views/registration/self-roster-update/self-roster-update-modal.service';

interface RegLink {
	key: string;
	label: string;
	icon: string;
	routerLink: string;
	queryParams?: Record<string, string>;
}

/**
 * Registration compound viewer — the self-assembling capture of the hand-authored
 * "Player & Coach Registration and Waiver Wizard" bulletin. A single panel that
 * CONDITIONALLY constructs its sections from the live pulse + the viewer's state:
 *
 *   • Self-Rostering — links for each self-registration role the director has open
 *   • Manage — the FEATURED self-roster-update (change team / uniform # / cancel),
 *     plus My Registration / Pay Balance for a viewer who already holds a reg
 *   • Rosters — the public roster view
 *
 * The self-roster-update is the support-call killer: it has no other hero presence,
 * and its modal handles its own login, so it's offered to everyone (anonymous
 * included — they sign in inside it). Each section renders only when it has content;
 * the whole panel self-hides when there's nothing to show.
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

	private readonly pulseService = inject(JobPulseService);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);
	private readonly sruModal = inject(SelfRosterUpdateModalService);

	private readonly pulse = computed(() => this.pulseService.pulse());
	private readonly base = computed(() => `/${this.jobPath()}`);

	// A viewer holding a regId is past the "register" stage (Player/Family sees
	// "My Registration" rather than a self-roster link). Anonymous still registers.
	private readonly registered = computed(() => !!this.auth.currentUser()?.regId);
	private readonly isPlayerOrFamily = computed(() => {
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

	// ── Manage section ──────────────────────────────────────────────────────────
	// The self-roster-update (change team / uniform # / cancel) is meaningful while
	// player registration is open — that's when self-service fixes save support calls.
	protected readonly showChange = computed(() =>
		this.allowedKeys().has('register-player') && !!this.pulse()?.playerRegistrationOpen);

	protected readonly manageLinks = computed<RegLink[]>(() => {
		const p = this.pulse();
		if (!p || !this.registered() || !this.isPlayerOrFamily()) return [];
		const allowed = this.allowedKeys();
		const base = this.base();
		const links: RegLink[] = [];
		if (allowed.has('my-registration')) {
			links.push({ key: 'my-registration', label: 'My Registration', icon: 'bi-person-vcard',
				routerLink: `${base}/registration/player`, queryParams: { step: 'players' } });
		}
		if (allowed.has('pay-balance') && (p.myRegistrationOwedTotal ?? 0) > 0) {
			links.push({ key: 'pay-balance', label: 'Pay Balance Due', icon: 'bi-cash-stack',
				routerLink: `${base}/registration/player`, queryParams: { step: 'payment' } });
		}
		return links;
	});

	protected readonly showManage = computed(() => this.showChange() || this.manageLinks().length > 0);

	// ── Rosters section ─────────────────────────────────────────────────────────
	protected readonly rostersLink = computed<RegLink | null>(() => {
		const p = this.pulse();
		if (!this.allowedKeys().has('rosters') || !p?.publicRostersAvailable) return null;
		return { key: 'rosters', label: 'View Public Rosters', icon: 'bi-card-checklist',
			routerLink: `${this.base()}/rosters/public` };
	});

	/** The panel self-hides when no section has content. */
	protected readonly hasContent = computed(() =>
		this.selfRosterLinks().length > 0 || this.showManage() || this.rostersLink() !== null);

	openSelfRosterUpdate(): void {
		this.sruModal.open(this.jobPath());
	}
}
