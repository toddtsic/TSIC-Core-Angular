import {
	afterNextRender,
	ChangeDetectionStrategy,
	Component,
	computed,
	ElementRef,
	inject,
	input,
	OnDestroy,
	signal
} from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot, Router } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { isTournament } from '@infrastructure/constants/job-type.constants';
import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { ViewScheduleService } from '@views/scheduling/view-schedule/services/view-schedule.service';
import { InlineGameClockComponent } from '@views/scheduling/view-schedule/components/inline-game-clock.component';
import { ActionHubComponent, HubItem } from '@layouts/components/action-hub/action-hub.component';

/**
 * The event's lifecycle position, derived from facts (schedule released + the
 * first/last game dates vs now), NOT from the director's registration toggles —
 * which are routinely left on after an event ends. Precedence is first-match in
 * the `phase` computed below.
 */
type EventPhase =
	| 'superseded'       // a live later-year sibling exists — page redirects to it
	| 'concluded'        // schedule published AND the last game day has fully passed
	| 'inSeason'         // schedule published AND the first game day has arrived
	| 'preEvent'         // schedule published, first game still in the future
	| 'registrationOpen' // registration (player/team) accepting, or a club rep with teams
	| 'planned'          // registration announced/planned but not yet open
	| 'preview';         // nothing actionable yet

/**
 * Which CTA keys are *eligible* in each phase. A candidate is built only when its
 * own data flag is set (store has items, schedule published, …); this set then
 * removes the ones that don't belong in the lifecycle stage. That filter is what
 * lets a finished event ignore a Register toggle a director left on.
 */
const CTAS_BY_PHASE: Record<EventPhase, ReadonlySet<string>> = {
	superseded: new Set(),
	preview: new Set(),
	planned: new Set(['store']),
	concluded: new Set(['pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters']),
	inSeason: new Set(['my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	preEvent: new Set(['my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	registrationOpen: new Set(['register-player', 'my-registration', 'pay-balance', 'register-team', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance'])
};

/** Per-phase emphasis order: the first key present in the resolved items leads. */
const PRIMARY_BY_PHASE: Record<EventPhase, readonly string[]> = {
	superseded: [],
	preview: [],
	concluded: ['pay-balance', 'view-schedule', 'rosters'],
	inSeason: ['pay-balance', 'view-schedule'],
	preEvent: ['pay-balance', 'view-schedule'],
	registrationOpen: ['pay-balance', 'my-registration', 'register-player', 'my-teams', 'register-team'],
	planned: ['store']
};

@Component({
	selector: 'app-job-landing',
	standalone: true,
	imports: [ClientBannerComponent, BulletinsComponent, InlineGameClockComponent, ActionHubComponent],
	templateUrl: './job-landing.component.html',
	styleUrl: './job-landing.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class JobLandingComponent implements OnDestroy {
	private readonly elRef = inject(ElementRef);
	private readonly jobService = inject(JobService);
	private readonly pulseService = inject(JobPulseService);
	private readonly auth = inject(AuthService);
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly scheduleService = inject(ViewScheduleService);
	private observer: IntersectionObserver | null = null;

	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

	readonly job = computed(() => this.jobService.currentJob());
	readonly jobId = computed(() => this.jobService.currentJob()?.jobId ?? '');

	// True from mount until the initial fill (job metadata + bulletins) settles.
	// Drives one big centered spinner so the page reveals all at once instead of
	// painting the banner first and dribbling bulletins in seconds later.
	private readonly bootstrapping = signal(true);
	readonly loading = computed(() => this.bootstrapping() || this.jobService.bulletinsLoading());

	// Pulse is the canonical source of "is X currently available" state. Both
	// PlayerRegistrationOpen and TeamRegistrationOpen are pure job-level flags
	// (no role gating); per-user state lives in the My* fields.
	readonly pulse = computed(() => this.pulseService.pulse());

	readonly activeJobPath = computed(() => {
		const fromInput = this.publicJobPath();
		if (fromInput) return fromInput;
		const user = this.auth.currentUser();
		if (user?.jobPath) return user.jobPath;
		let r: ActivatedRouteSnapshot | null = this.route.snapshot;
		while (r) {
			const jp = r.paramMap.get('jobPath');
			if (jp) return jp;
			r = r.parent;
		}
		return '';
	});

	// The event's lifecycle phase, derived from FACTS (schedule released + first/
	// last game dates vs today), not from the director's registration toggles.
	// First match wins. This is the core of the "smart" hero: a finished event
	// (concluded) or an in-progress one (inSeason) outranks the registration
	// flags, so a Register toggle left on after the last game can't resurrect a
	// "Register Player" card. Local start-of-day comparisons (wall-clock convention).
	readonly phase = computed<EventPhase>(() => {
		const p = this.pulse();
		if (!p) return 'preview';
		if (p.supersededByLaterEvent) return 'superseded';
		const today = this.startOfDay(new Date()).getTime();
		const lastGame = p.lastGameDate ? this.startOfDay(new Date(p.lastGameDate)).getTime() : null;
		// Concluded = released AND the last game day has fully passed (strict <,
		// so the last game day itself still reads in-season).
		if (p.schedulePublished && lastGame !== null && lastGame < today) return 'concluded';
		const firstGame = p.firstGameDate ? this.startOfDay(new Date(p.firstGameDate)).getTime() : null;
		if (p.schedulePublished && firstGame !== null && firstGame <= today) return 'inSeason';
		if (p.schedulePublished) return 'preEvent';
		if (p.playerRegistrationOpen || p.teamRegistrationOpen || (p.myClubRepTeamCount ?? 0) > 0) return 'registrationOpen';
		if (p.playerRegistrationPlanned || p.adultRegistrationPlanned || p.playerRegOpensSoonest != null) return 'planned';
		return 'preview';
	});

	/** Drives the inline "this event has concluded" notice (decoupled from supersession). */
	readonly isConcluded = computed(() => this.phase() === 'concluded');

	// Grounded landing CTAs (LandingCta placement), derived from the live pulse —
	// the canonical "what's open" snapshot. INTERIM: this thin pulse→action mapping
	// runs client-side so the hero works in-context now; the full server nav-merge
	// (role-gating + personalization + account menu) supersedes it. Absolute,
	// jobPath-prefixed routerLinks (the jobPath is in the path, so it's preserved).
	readonly heroActions = computed<HubItem[]>(() => {
		// Admins navigate via the dense nav chrome — the public CTA hero is for
		// anonymous + non-admin viewers only. A Superuser has no use for a
		// "Register Player" card, so the hero stays empty for them.
		if (this.auth.isAdmin()) return [];
		const p = this.pulse();
		const jp = this.activeJobPath();
		if (!p || !jp) return [];
		const phase = this.phase();
		const allowed = CTAS_BY_PHASE[phase];
		if (!allowed.size) return [];
		const base = `/${jp}`;
		// A viewer who already holds a registration in this job (has a regId) is
		// past the "register" stage: a Player/Family sees "My Registration" in
		// place of "Register Player". Anonymous viewers still register.
		const user = this.auth.currentUser();
		const registered = !!user?.regId;
		const isPlayerOrFamily = user?.role === Roles.Player || user?.role === Roles.Family;

		// Candidate destinations, gated ONLY by data availability — never by the
		// lifecycle. The phase's allowed-set (above) then drops the ones that don't
		// belong in this stage; that filter is what sees through a stale toggle.
		const candidates: HubItem[] = [];
		if (p.playerRegistrationOpen && !registered) {
			// Tournament: the team registers; a player self-rosters onto one.
			const playerLabel = isTournament(this.job()?.jobTypeId) ? 'Self-Roster Player' : 'Register Player';
			candidates.push({ key: 'register-player', label: playerLabel, icon: 'bi-person-plus', routerLink: `${base}/registration/player` });
		}
		if (registered && isPlayerOrFamily) {
			candidates.push({ key: 'my-registration', label: 'My Registration', icon: 'bi-person-badge', routerLink: `${base}/registration/player`, queryParams: { step: 'players' } });
		}
		// Pay Balance Due — a registered Player/Family that still owes. Survives into
		// concluded (you can always settle a balance after the event).
		if (registered && isPlayerOrFamily && (p.myRegistrationOwedTotal ?? 0) > 0) {
			candidates.push({ key: 'pay-balance', label: 'Pay Balance Due', icon: 'bi-cash-stack', routerLink: `${base}/registration/player`, queryParams: { step: 'payment' } });
		}
		if (p.teamRegistrationOpen && !registered) {
			candidates.push({ key: 'register-team', label: 'Register Team', icon: 'bi-people', routerLink: `${base}/registration/team` });
		}
		// A Club Rep with >=1 team manages/reviews them via "My Teams" (deep-link to
		// the teams step). myClubRepTeamCount is only populated for a Club Rep scoped
		// to this job, so > 0 encodes both role and has-teams.
		if ((p.myClubRepTeamCount ?? 0) > 0) {
			candidates.push({ key: 'my-teams', label: 'My Teams', icon: 'bi-people', routerLink: `${base}/registration/team`, queryParams: { step: 'teams' } });
		}
		if (p.schedulePublished) {
			candidates.push({ key: 'view-schedule', label: phase === 'concluded' ? 'Final Schedule' : 'View Schedule', icon: 'bi-calendar-event', routerLink: `${base}/schedule` });
		}
		if (p.storeHasActiveItems) {
			candidates.push({ key: 'store', label: 'Store', icon: 'bi-bag', routerLink: `${base}/store` });
		}
		// Public "Rosters" card gates on public-roster availability (bRestrictPublicRosters),
		// NOT allowRosterViewPlayer (that's a logged-in user's OWN-roster gate).
		if (p.publicRostersAvailable) {
			candidates.push({ key: 'rosters', label: 'Rosters', icon: 'bi-card-checklist', routerLink: `${base}/rosters/public` });
		}
		// RegSaver is a registered-player/team benefit. Shown broadly as a discovery
		// affordance (anonymous prospects included — the hint sets the expectation),
		// but suppressed once it no longer applies. The My* signals are null for
		// anonymous, so the `!==` checks keep the discovery card visible for them and
		// only hide it for the registrant who's already covered.
		if (p.offerPlayerRegsaverInsurance && p.myHasPurchasedPlayerRegsaver !== true) {
			candidates.push({ key: 'player-insurance', label: 'Player RegSaver', hint: 'For registered players', icon: 'bi-shield-check', routerLink: `${base}/PlayerVIUpdate` });
		}
		if (p.offerTeamRegsaverInsurance && p.myClubRepHasTeamWithoutRegsaver !== false) {
			candidates.push({ key: 'team-insurance', label: 'Team RegSaver', hint: 'For registered teams', icon: 'bi-shield-check', routerLink: `${base}/ClubRepVIUpdate` });
		}

		// Membership and order come straight from the pulse-grounded candidates,
		// filtered to what this lifecycle phase allows.
		const items = candidates.filter(i => allowed.has(i.key));
		if (!items.length) return items;

		// Primary = the first phase-preferred key present; else the first item.
		// Floated to the front with emphasis (immutable copy — feeds an OnPush input).
		const prefs = PRIMARY_BY_PHASE[phase];
		let primaryIdx = -1;
		for (const key of prefs) {
			primaryIdx = items.findIndex(i => i.key === key);
			if (primaryIdx >= 0) break;
		}
		if (primaryIdx < 0) primaryIdx = 0;
		if (primaryIdx > 0) {
			const [primary] = items.splice(primaryIdx, 1);
			items.unshift({ ...primary, emphasis: 'primary' });
		} else {
			items[0] = { ...items[0], emphasis: 'primary' };
		}
		return items;
	});

	// Phase-aware status line for the public hero (anonymous-leaning; a logged-in
	// registrant's personalized deadline is a later slice). Pre-event shows when
	// the games begin; registration-open shows the closing/opening countdown;
	// planned announces upcoming registration. Concluded/in-season show no line
	// (the concluded notice and the inline game clock carry those). Tone softens
	// with distance. The open-ended far-future deadline is intentionally suppressed.
	readonly heroStatus = computed<{ text: string; tone: 'open' | 'upcoming' } | null>(() => {
		if (this.auth.isAdmin()) return null;
		const p = this.pulse();
		if (!p) return null;
		switch (this.phase()) {
			case 'preEvent': {
				const text = p.firstGameDate ? this.formatEventStart(p.firstGameDate) : null;
				return text ? { text, tone: 'upcoming' } : null;
			}
			case 'planned': {
				const dated = p.playerRegOpensSoonest ? this.formatDeadline(p.playerRegOpensSoonest, 'opens') : null;
				return { text: dated ?? 'Registration opening soon', tone: 'upcoming' };
			}
			case 'registrationOpen': {
				// Suppressed for anyone holding a regId (they've registered).
				if (this.auth.currentUser()?.regId) return null;
				if (p.playerRegClosesSoonest) {
					const text = this.formatDeadline(p.playerRegClosesSoonest, 'closes');
					return text ? { text, tone: 'open' } : null;
				}
				if (p.playerRegOpensSoonest) {
					const text = this.formatDeadline(p.playerRegOpensSoonest, 'opens');
					return text ? { text, tone: 'upcoming' } : null;
				}
				return null;
			}
			default:
				return null;
		}
	});

	/** Local start-of-day (calendar-day comparisons, not raw 24h spans). */
	private startOfDay(d: Date): Date {
		const x = new Date(d);
		x.setHours(0, 0, 0, 0);
		return x;
	}

	// Day-granularity phrasing that softens as the deadline recedes (~14-day
	// urgency threshold). Returns null past the threshold: a far-off date isn't
	// urgent, so no line shows — the Register CTA itself signals availability.
	private formatDeadline(iso: string, mode: 'closes' | 'opens'): string | null {
		const target = new Date(iso);
		if (Number.isNaN(target.getTime())) return null;
		const days = Math.round((this.startOfDay(target).getTime() - this.startOfDay(new Date()).getTime()) / 86_400_000);
		if (mode === 'closes') {
			if (days <= 0) return 'Registration closes today';
			if (days === 1) return 'Registration closes tomorrow';
			if (days <= 14) return `Registration closes in ${days} days`;
			return null;
		}
		if (days <= 0) return 'Registration opens today';
		if (days === 1) return 'Registration opens tomorrow';
		if (days <= 14) return `Registration opens in ${days} days`;
		return null;
	}

	// "Event begins …" for the pre-event phase. Unlike registration, a far-future
	// game date is still worth showing (it's the event itself), so it keeps the
	// dated form past the urgency window.
	private formatEventStart(iso: string): string | null {
		const target = new Date(iso);
		if (Number.isNaN(target.getTime())) return null;
		const days = Math.round((this.startOfDay(target).getTime() - this.startOfDay(new Date()).getTime()) / 86_400_000);
		if (days <= 0) return 'Event begins today';
		if (days === 1) return 'Event begins tomorrow';
		if (days <= 14) return `Event begins in ${days} days`;
		const dateLabel = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(target);
		return `Event begins ${dateLabel}`;
	}

	// Stale event with a live later-year sibling — collapses the entire page
	// to a single callout that redirects to the live event.
	readonly isSuperseded = computed(() => !!this.pulse()?.supersededByLaterEvent);
	readonly supersedingName = computed(() => this.pulse()?.supersededByLaterEvent?.jobName ?? '');

	// Probed once on mount; true when this job has at least one upcoming or
	// in-progress RR/PO game. Drives the inline game clock in the toolbar.
	private readonly hasGameClockGames = signal(false);
	readonly showInlineClock = computed(() =>
		!!this.pulse()?.schedulePublished && !!this.jobId() && this.hasGameClockGames()
	);

	/**
	 * Cross-event redirect for the superseded callout. Clears local auth so
	 * the live event's landing loads anonymously — otherwise the JWT for the
	 * stale event bleeds context (myAssignedTeamId, name, etc.) into the new
	 * landing's pulse fetch.
	 */
	goToSupersedingEvent(): void {
		const target = this.pulse()?.supersededByLaterEvent;
		if (!target) return;
		this.auth.logoutLocal();
		this.router.navigate(['/', target.jobPath]);
	}

	constructor() {
		afterNextRender(() => {
			const jp = this.activeJobPath();
			if (jp) {
				this.jobService.fetchJobMetadata(jp).subscribe({
					next: (job) => {
						this.jobService.setJob(job);
						this.jobService.loadBulletins(jp);
						this.probeGameClock(job.jobId);
						this.bootstrapping.set(false);
					},
					error: () => this.bootstrapping.set(false)
				});
			} else {
				this.bootstrapping.set(false);
			}
			this.initRevealAnimations();
		});
	}

	ngOnDestroy(): void {
		this.observer?.disconnect();
		this.observer = null;
	}

	private probeGameClock(jobId: string): void {
		if (!jobId) return;
		this.scheduleService.getActiveGames(jobId).subscribe({
			next: (data) => {
				const has = (data.availableRRGameData?.length ?? 0) > 0
					|| (data.availablePOGameData?.length ?? 0) > 0;
				this.hasGameClockGames.set(has);
			},
			error: () => this.hasGameClockGames.set(false)
		});
	}

	private initRevealAnimations(): void {
		const reduced = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
		if (reduced) {
			this.elRef.nativeElement.querySelectorAll('.reveal')
				.forEach((el: Element) => el.classList.add('revealed'));
			return;
		}

		this.observer = new IntersectionObserver(
			(entries) => entries.forEach(entry => {
				if (entry.isIntersecting) {
					entry.target.classList.add('revealed');
					this.observer?.unobserve(entry.target);
				}
			}),
			{ root: null, rootMargin: '0px 0px -40px 0px', threshold: 0.15 }
		);

		this.elRef.nativeElement.querySelectorAll('.reveal')
			.forEach((el: Element) => this.observer!.observe(el));
	}
}
