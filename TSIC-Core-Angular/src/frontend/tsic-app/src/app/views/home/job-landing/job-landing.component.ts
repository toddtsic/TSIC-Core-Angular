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
	private readonly auth = inject(AuthService);
	private readonly route = inject(ActivatedRoute);
	private observer: IntersectionObserver | null = null;

	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

	readonly job = computed(() => this.jobService.currentJob());

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
		const expiry = this.job()?.expiryUsers;
		if (!expiry) return 'unknown';
		const expiryMs = Date.parse(expiry);
		if (Number.isNaN(expiryMs)) return 'unknown';
		const now = Date.now();
		return now < expiryMs ? 'registration' : 'pre-season';
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
		const expiry = this.job()?.expiryUsers;
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

	readonly ctaLabel = computed(() => {
		switch (this.phase()) {
			case 'registration': return 'Register';
			case 'pre-season':
			case 'in-season': return 'View Schedule';
			case 'post-season': return 'View Results';
			default: return 'Explore Event';
		}
	});

	readonly ctaPath = computed(() => {
		const jp = this.activeJobPath();
		if (!jp) return ['/'];
		switch (this.phase()) {
			case 'registration': return ['/', jp, 'register'];
			case 'pre-season':
			case 'in-season':
			case 'post-season': return ['/', jp, 'schedule-hub'];
			default: return ['/', jp];
		}
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
