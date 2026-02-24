import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink, ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { environment } from '@environments/environment';
import type { JobPulseDto } from '@core/api';

interface PulseCard {
	icon: string;
	title: string;
	cta?: string;
	link?: string;
	style: 'active' | 'coming-soon';
}

@Component({
	selector: 'app-job-pulse-widget',
	standalone: true,
	imports: [RouterLink],
	templateUrl: './job-pulse-widget.component.html',
	styleUrl: './job-pulse-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobPulseWidgetComponent implements OnInit {
	private readonly http = inject(HttpClient);
	private readonly auth = inject(AuthService);
	private readonly route = inject(ActivatedRoute);

	readonly pulse = signal<JobPulseDto | null>(null);
	readonly loading = signal(true);

	readonly cards = computed<PulseCard[]>(() => {
		const p = this.pulse();
		if (!p || p.publicSuspended) return [];

		const result: PulseCard[] = [];
		const jobPath = this.resolveJobPath();

		if (p.playerRegistrationOpen) {
			result.push({
				icon: 'bi-person-plus',
				title: 'Player Registration is Open',
				cta: 'Register Now',
				link: `/${jobPath}/family-account`,
				style: 'active',
			});
		}

		if (p.teamRegistrationOpen) {
			result.push({
				icon: 'bi-shield-plus',
				title: 'Team Registration is Open',
				cta: 'Register Your Team',
				link: `/${jobPath}/register-team`,
				style: 'active',
			});
		}

		if (p.storeEnabled && p.storeHasActiveItems) {
			result.push({
				icon: 'bi-bag',
				title: 'Merch Store is Open',
				cta: 'Browse Store',
				link: `/${jobPath}/store`,
				style: 'active',
			});
		}

		if (p.schedulePublished) {
			result.push({
				icon: 'bi-calendar-check',
				title: 'Schedules Are Live',
				cta: 'View Schedule',
				link: `/${jobPath}/scheduling/view-schedule`,
				style: 'active',
			});
		}

		if (p.playerRegistrationPlanned) {
			result.push({
				icon: 'bi-clock',
				title: 'Player Registration Coming Soon',
				style: 'coming-soon',
			});
		}

		if (p.adultRegistrationPlanned) {
			result.push({
				icon: 'bi-clock',
				title: 'Adult Registration Coming Soon',
				style: 'coming-soon',
			});
		}

		return result;
	});

	readonly hasContent = computed(() => this.cards().length > 0);

	ngOnInit(): void {
		const jobPath = this.resolveJobPath();
		if (!jobPath) {
			this.loading.set(false);
			return;
		}

		this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
			.subscribe({
				next: d => { this.pulse.set(d); this.loading.set(false); },
				error: () => this.loading.set(false),
			});
	}

	private resolveJobPath(): string {
		const user = this.auth.currentUser();
		if (user?.jobPath) return user.jobPath;
		let r: ActivatedRouteSnapshot | null = this.route.snapshot;
		while (r) {
			const jp = r.paramMap.get('jobPath');
			if (jp) return jp;
			r = r.parent;
		}
		return '';
	}
}
