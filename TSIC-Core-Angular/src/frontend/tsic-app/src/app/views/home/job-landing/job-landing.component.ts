import {
	afterNextRender,
	ChangeDetectionStrategy,
	Component,
	computed,
	ElementRef,
	inject,
	input,
	OnDestroy
} from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot, RouterLink } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';

type JobPhase = 'pre-registration' | 'registration' | 'pre-season' | 'in-season' | 'post-season' | 'unknown';

@Component({
	selector: 'app-job-landing',
	standalone: true,
	imports: [RouterLink, ClientBannerComponent, BulletinsComponent],
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
	private observer: IntersectionObserver | null = null;

	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

	readonly job = computed(() => this.jobService.currentJob());

	// Pulse is the canonical source for "is X currently available" gating —
	// it's role-aware (e.g. PlayerRegistrationOpen = isFamily && allowPlayer)
	// and computed server-side. Metadata flags alone aren't sufficient.
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

	readonly phase = computed<JobPhase>(() => {
		const p = this.pulse();
		if (!p) return 'unknown';
		if (p.playerRegistrationOpen || p.teamRegistrationOpen) return 'registration';
		if (p.schedulePublished) return 'in-season';
		if (p.playerRegistrationPlanned || p.adultRegistrationPlanned) return 'pre-registration';
		return 'unknown';
	});

	readonly phaseIcon = computed(() => {
		switch (this.phase()) {
			case 'registration': return 'bi-hourglass-split';
			case 'pre-season': return 'bi-calendar2-check';
			case 'in-season': return 'bi-broadcast';
			case 'post-season': return 'bi-trophy-fill';
			case 'pre-registration': return 'bi-clock';
			default: return '';
		}
	});

	/** Complementary info to the CTA — a deadline or date, not a re-statement of the phase. */
	readonly phaseSubtext = computed(() => {
		const p = this.pulse();
		// Superseded events have a stale deadline that no longer matters — the
		// CTA already redirects to the live sibling event, so any "Closes …"
		// text would be misleading.
		if (p?.supersededByLaterEvent) return '';

		const expiry = p?.registrationExpiry;
		if (!expiry) return '';
		const ms = Date.parse(expiry);
		if (Number.isNaN(ms)) return '';
		const days = Math.ceil((ms - Date.now()) / 86_400_000);
		const dateStr = new Date(ms).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

		switch (this.phase()) {
			case 'registration':
				if (days <= 0) return 'Closing today';
				if (days === 1) return 'Closes tomorrow';
				if (days <= 14) return `Closes in ${days} days`;
				return `Closes ${dateStr}`;
			default:
				return '';
		}
	});

	/**
	 * Toolbar CTAs. Reads pulse — the canonical source for "is open right now
	 * for this user." Pulse encodes role-awareness (PlayerRegistrationOpen is
	 * computed server-side as `isFamily && allowPlayer`; TeamRegistrationOpen
	 * is `!isFamily && allowTeam`), so anonymous and admin users naturally see
	 * only the registration types relevant to them.
	 *
	 * - both open  → two buttons ("Register Player" + "Register Team")
	 * - one open   → single "Register" button to that type's route
	 * - none open  → schedule (if published) or "Explore Event"
	 *
	 * Routes match app.routes.ts (line 73+): `/<jobPath>/registration/{player,team}`.
	 */
	readonly ctas = computed<readonly { readonly label: string; readonly path: readonly (string)[] }[]>(() => {
		const jp = this.activeJobPath();
		if (!jp) return [{ label: 'Explore Event', path: ['/'] }];

		const p = this.pulse();

		// Superseded by a later-year sibling event — redirect intent to the
		// live event. The job name carries the year, so the CTA stays
		// year-agnostic (could be next year, could be several years out).
		const newer = p?.supersededByLaterEvent;
		if (newer) {
			return [{ label: `Register for ${newer.jobName}`, path: ['/', newer.jobPath] }];
		}

		const playerOpen = p?.playerRegistrationOpen === true;
		const teamOpen = p?.teamRegistrationOpen === true;

		if (playerOpen && teamOpen) {
			return [
				{ label: 'Register Player', path: ['/', jp, 'registration', 'player'] },
				{ label: 'Register Team', path: ['/', jp, 'registration', 'team'] }
			];
		}
		if (playerOpen) return [{ label: 'Register', path: ['/', jp, 'registration', 'player'] }];
		if (teamOpen)   return [{ label: 'Register', path: ['/', jp, 'registration', 'team'] }];

		// Neither registration type open for this user — fall back to a phase-appropriate CTA.
		if (p?.schedulePublished) return [{ label: 'View Schedule', path: ['/', jp, 'schedule-hub'] }];
		return [{ label: 'Explore Event', path: ['/', jp] }];
	});

	constructor() {
		afterNextRender(() => {
			const jp = this.activeJobPath();
			if (jp) {
				this.jobService.fetchJobMetadata(jp).subscribe({
					next: (job) => {
						this.jobService.setJob(job);
						this.jobService.loadBulletins(jp);
					}
				});
			}
			this.initRevealAnimations();
		});
	}

	ngOnDestroy(): void {
		this.observer?.disconnect();
		this.observer = null;
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
