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
import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { ViewScheduleService } from '@views/scheduling/view-schedule/services/view-schedule.service';
import { InlineGameClockComponent } from '@views/scheduling/view-schedule/components/inline-game-clock.component';
import { ActionHubComponent, HubItem } from '@layouts/components/action-hub/action-hub.component';

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
		const base = `/${jp}`;
		// A viewer who already holds a registration in this job (has a regId) is
		// past the "register" stage: a Player/Family sees "My Registration" in
		// place of "Register Player" (same target as the top-right menu), and the
		// public register CTAs are suppressed. Anonymous viewers still register.
		const user = this.auth.currentUser();
		const registered = !!user?.regId;
		const isPlayerOrFamily = user?.role === Roles.Player || user?.role === Roles.Family;
		const items: HubItem[] = [];
		if (p.playerRegistrationOpen) {
			if (registered && isPlayerOrFamily) {
				items.push({ key: 'my-registration', label: 'My Registration', icon: 'bi-person-badge', routerLink: `${base}/registration/player`, queryParams: { step: 'players' } });
			} else if (!registered) {
				items.push({ key: 'register-player', label: 'Register Player', icon: 'bi-person-plus', routerLink: `${base}/registration/player` });
			}
		}
		// Pay Balance Due — a registered Player/Family that still owes. Available
		// regardless of the registration window (you can always settle a balance).
		if (registered && isPlayerOrFamily && (p.myRegistrationOwedTotal ?? 0) > 0) {
			items.push({ key: 'pay-balance', label: 'Pay Balance Due', icon: 'bi-cash-stack', routerLink: `${base}/registration/player`, queryParams: { step: 'payment' } });
		}

		// Once schedules publish, team rosters lock — suppress Register Team.
		if (p.teamRegistrationOpen && !p.schedulePublished && !registered) {
			items.push({ key: 'register-team', label: 'Register Team', icon: 'bi-people', routerLink: `${base}/registration/team` });
		}
		if (p.schedulePublished) {
			items.push({ key: 'view-schedule', label: 'View Schedule', icon: 'bi-calendar-event', routerLink: `${base}/schedule` });
		}
		if (p.storeHasActiveItems) {
			items.push({ key: 'store', label: 'Store', icon: 'bi-bag', routerLink: `${base}/store` });
		}
		if (p.allowRosterViewPlayer) {
			items.push({ key: 'rosters', label: 'Rosters', icon: 'bi-card-checklist', routerLink: `${base}/rosters/public` });
		}
		if (p.offerPlayerRegsaverInsurance) {
			items.push({ key: 'player-insurance', label: 'Insurance Update', icon: 'bi-shield-check', routerLink: `${base}/PlayerVIUpdate` });
		}

		// The single emphasized (primary) action is chosen from the event's
		// lifecycle stage, not a fixed action: once schedules publish the event
		// is in-season and the schedule dominates visitor intent; before that,
		// registration leads. The primary is floated to the front of the row.
		// A balance due is the urgent action — it leads ahead of the lifecycle
		// pick. Otherwise the schedule (in-season) or registration (pre-season).
		const primaryKey = items.some(i => i.key === 'pay-balance')
			? 'pay-balance'
			: p.schedulePublished
				? 'view-schedule'
				: p.playerRegistrationOpen
					? (registered ? 'my-registration' : 'register-player')
					: p.teamRegistrationOpen
						? 'register-team'
						: items[0]?.key;

		// Fall back to the first available action when the lifecycle-preferred
		// primary isn't present (e.g. a registered viewer with register suppressed).
		let primaryIdx = items.findIndex(i => i.key === primaryKey);
		if (primaryIdx < 0 && items.length) primaryIdx = 0;
		if (primaryIdx > 0) {
			const [primary] = items.splice(primaryIdx, 1);
			items.unshift({ ...primary, emphasis: 'primary' });
		} else if (primaryIdx === 0) {
			items[0] = { ...items[0], emphasis: 'primary' };
		}
		return items;
	});

	// Registration deadline countdown for the public hero. INTERIM (anonymous
	// only): a logged-in registrant gets a personalized "next deadline" in a
	// later slice; for now we suppress it for anyone holding a regId. Tied to the
	// same gate as the Register Player CTA (playerRegistrationOpen), driven by the
	// pulse's aggregated team-window dates. Tone softens with distance.
	readonly registrationCountdown = computed<{ text: string; icon: string } | null>(() => {
		if (this.auth.isAdmin()) return null;
		const p = this.pulse();
		if (!p || !p.playerRegistrationOpen) return null;
		if (this.auth.currentUser()?.regId) return null;
		if (p.playerRegClosesSoonest) {
			const text = this.formatDeadline(p.playerRegClosesSoonest, 'closes');
			return text ? { text, icon: 'bi-clock-history' } : null;
		}
		if (p.playerRegOpensSoonest) {
			const text = this.formatDeadline(p.playerRegOpensSoonest, 'opens');
			return text ? { text, icon: 'bi-calendar-event' } : null;
		}
		return null;
	});

	// Day-granularity phrasing that softens as the deadline recedes (~14-day
	// urgency threshold — easy to tune). Calendar-day diff, not raw 24h spans.
	private formatDeadline(iso: string, mode: 'closes' | 'opens'): string | null {
		const target = new Date(iso);
		if (Number.isNaN(target.getTime())) return null;
		const startOfToday = new Date();
		startOfToday.setHours(0, 0, 0, 0);
		const startOfTarget = new Date(target);
		startOfTarget.setHours(0, 0, 0, 0);
		const days = Math.round((startOfTarget.getTime() - startOfToday.getTime()) / 86_400_000);
		const dateLabel = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(target);
		if (mode === 'closes') {
			if (days <= 0) return 'Registration closes today';
			if (days === 1) return 'Registration closes tomorrow';
			if (days <= 14) return `Registration closes in ${days} days`;
			return `Registration open through ${dateLabel}`;
		}
		if (days <= 0) return 'Registration opens today';
		if (days === 1) return 'Registration opens tomorrow';
		if (days <= 14) return `Registration opens in ${days} days`;
		return `Registration opens ${dateLabel}`;
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
