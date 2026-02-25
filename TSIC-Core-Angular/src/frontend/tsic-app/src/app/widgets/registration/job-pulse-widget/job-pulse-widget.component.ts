import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink, ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { environment } from '@environments/environment';
import type { JobPulseDto } from '@core/api';

interface PulseCard {
	icon: string;
	title: string;
	subtitle?: string;
	cta?: string;
	link?: string;
	style: 'active' | 'coming-soon';
	accent: 'primary' | 'success' | 'warning' | 'info' | 'muted';
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
		if (!p) return [];

		const result: PulseCard[] = [];
		const jobPath = this.resolveJobPath();

		if (p.playerRegistrationOpen) {
			result.push({
				icon: 'bi-person-plus-fill',
				title: 'Player Registration',
				subtitle: 'Now accepting players',
				cta: 'Register a Player',
				link: `/${jobPath}/register-player`,
				style: 'active',
				accent: 'success',
			});
		}

		if (p.teamRegistrationOpen) {
			result.push({
				icon: 'bi-shield-fill-plus',
				title: 'Team Registration',
				subtitle: 'Now accepting teams',
				cta: 'Register Your Team',
				link: `/${jobPath}/register-team`,
				style: 'active',
				accent: 'primary',
			});
		}

		if (p.storeEnabled && p.storeHasActiveItems) {
			result.push({
				icon: 'bi-bag-fill',
				title: 'Merch Store',
				subtitle: 'Gear & apparel available',
				cta: 'Browse Store',
				link: `/${jobPath}/store`,
				style: 'active',
				accent: 'warning',
			});
		}

		if (p.schedulePublished) {
			result.push({
				icon: 'bi-calendar2-check-fill',
				title: 'Game Schedules',
				subtitle: 'Schedules are live',
				cta: 'View Schedule',
				link: `/${jobPath}/scheduling/view-schedule`,
				style: 'active',
				accent: 'info',
			});
		}

		if (p.playerRegistrationPlanned) {
			result.push({
				icon: 'bi-person-plus',
				title: 'Player Registration',
				subtitle: 'Coming soon — stay tuned!',
				style: 'coming-soon',
				accent: 'muted',
			});
		}

		if (p.adultRegistrationPlanned) {
			result.push({
				icon: 'bi-person-badge',
				title: 'Adult Registration',
				subtitle: 'Coming soon — stay tuned!',
				style: 'coming-soon',
				accent: 'muted',
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
