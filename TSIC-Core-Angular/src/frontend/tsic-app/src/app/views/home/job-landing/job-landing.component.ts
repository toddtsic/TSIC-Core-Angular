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
import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { derivePhase, EventPhase } from '@shared/landing/landing-phase';

@Component({
	selector: 'app-job-landing',
	standalone: true,
	imports: [ClientBannerComponent, BulletinsComponent],
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
	private observer: IntersectionObserver | null = null;

	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

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

	// The event's lifecycle phase, derived from FACTS via the shared resolver (same
	// pulse → same phase everywhere). Used here for the status line + concluded/
	// superseded notices; the Smart Bulletins band uses the same resolver to gate
	// its CTAs, so the two can never drift apart.
	readonly phase = computed<EventPhase>(() => derivePhase(this.pulse(), new Date()));

	// Phase-aware status line for the public hero (anonymous-leaning; a logged-in
	// registrant's personalized deadline is a later slice). Pre-event shows when
	// the games begin; registration-open shows the closing/opening countdown;
	// planned announces upcoming registration. Concluded/in-season show no line
	// (the concluded notice and the inline game clock carry those). Tone softens
	// with distance. The open-ended far-future deadline is intentionally suppressed.
	readonly heroStatus = computed<{ text: string; tone: 'open' | 'upcoming' } | null>(() => {
		// Admins land on this public page now; they see the anonymous public line (same
		// publicView treatment as the Smart Bulletins band) so their preview is faithful.
		const pub = this.auth.isAdmin();
		const p = this.pulse();
		if (!p) return null;
		switch (this.phase()) {
			case 'preEvent': {
				const text = p.firstGameDate ? this.formatEventStart(p.firstGameDate) : null;
				return text ? { text, tone: 'upcoming' } : null;
			}
			// 'planned' messaging now lives in the Smart Bulletins band's Event Status
			// bulletin (so it shows on the dashboard too) — no thin status line here.
			case 'planned':
				return null;
			case 'registrationOpen': {
				// Suppressed for anyone holding a regId (they've registered) — except an
				// admin in publicView, who sees the public line regardless of their own reg.
				if (!pub && this.auth.currentUser()?.regId) return null;
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
