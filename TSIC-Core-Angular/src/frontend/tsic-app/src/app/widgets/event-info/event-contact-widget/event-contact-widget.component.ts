import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { AuthService } from '@infrastructure/services/auth.service';
import type { EventContactDto } from '@core/api';

@Component({
	selector: 'app-event-contact-widget',
	standalone: true,
	templateUrl: './event-contact-widget.component.html',
	styleUrl: './event-contact-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventContactWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);
	private readonly route = inject(ActivatedRoute);

	readonly data = signal<EventContactDto | null>(null);
	readonly hasError = signal(false);

	readonly fullName = computed(() => {
		const d = this.data();
		if (!d) return '';
		return `${d.firstName} ${d.lastName}`.trim();
	});

	readonly mailtoLink = computed(() => {
		const d = this.data();
		return d?.email ? `mailto:${d.email}` : '';
	});

	ngOnInit(): void {
		const call$ = this.auth.isAuthenticated()
			? this.svc.getEventContact()
			: this.svc.getPublicEventContact(this.resolveJobPath());

		call$.subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
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
