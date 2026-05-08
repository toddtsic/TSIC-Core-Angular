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
import { ActivatedRoute, ActivatedRouteSnapshot, Router, RouterLink } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { ViewScheduleService } from '@views/scheduling/view-schedule/services/view-schedule.service';
import { InlineGameClockComponent } from '@views/scheduling/view-schedule/components/inline-game-clock.component';

type JobPhase = 'pre-registration' | 'registration' | 'in-season' | 'unknown';

@Component({
	selector: 'app-job-landing',
	standalone: true,
	imports: [RouterLink, ClientBannerComponent, BulletinsComponent, InlineGameClockComponent],
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

	readonly phase = computed<JobPhase>(() => {
		const p = this.pulse();
		if (!p) return 'unknown';
		if (p.playerRegistrationOpen || p.teamRegistrationOpen) return 'registration';
		if (p.schedulePublished) return 'in-season';
		if (p.playerRegistrationPlanned || p.adultRegistrationPlanned) return 'pre-registration';
		return 'unknown';
	});

	// Stale event with a live later-year sibling — collapses the entire page
	// to a single callout that redirects to the live event.
	readonly isSuperseded = computed(() => !!this.pulse()?.supersededByLaterEvent);
	readonly supersedingName = computed(() => this.pulse()?.supersededByLaterEvent?.jobName ?? '');

	// Toolbar only renders during active phases (registration or in-season).
	// Pre-registration, unknown, and the no-jobPath case render banner + bulletins
	// without a toolbar; superseded replaces the entire page (handled separately).
	readonly showToolbar = computed(() => {
		if (!this.activeJobPath() || !this.pulse() || this.isSuperseded()) return false;
		const ph = this.phase();
		return ph === 'registration' || ph === 'in-season';
	});

	// Probed once on mount; true when this job has at least one upcoming or
	// in-progress RR/PO game. Drives the inline game clock in the toolbar.
	private readonly hasGameClockGames = signal(false);
	readonly showInlineClock = computed(() =>
		!!this.pulse()?.schedulePublished && !!this.jobId() && this.hasGameClockGames()
	);

	readonly ctas = computed<readonly { readonly label: string; readonly path: readonly string[] }[]>(() => {
		const jp = this.activeJobPath();
		if (!jp) return [];

		const p = this.pulse();
		const schedulePublished = p?.schedulePublished === true;
		const playerOpen = p?.playerRegistrationOpen === true;
		// Once schedules are published, teams are locked into divisions/brackets —
		// adding new teams would disrupt the schedule. Suppress Register Team
		// regardless of the underlying flag, and replace it with a View Schedule
		// link in the same slot. (Player reg can still run; rostering players
		// onto already-scheduled teams is normal in-season.)
		const teamWouldOpen = p?.teamRegistrationOpen === true;
		const teamSuppressedBySchedule = teamWouldOpen && schedulePublished;
		const teamOpen = teamWouldOpen && !schedulePublished;

		const out: { readonly label: string; readonly path: readonly string[] }[] = [];
		if (playerOpen) out.push({ label: 'Register Player', path: ['/', jp, 'registration', 'player'] });
		if (teamOpen)   out.push({ label: 'Register Team',   path: ['/', jp, 'registration', 'team'] });

		// View Schedule fills the slot a suppressed Register Team would have
		// occupied; also shown standalone when nothing else qualifies but
		// schedules are out (in-season post-registration).
		if (teamSuppressedBySchedule || (out.length === 0 && schedulePublished)) {
			out.push({ label: 'View Schedule', path: ['/', jp, 'scheduling', 'view-schedule'] });
		}
		return out;
	});

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
