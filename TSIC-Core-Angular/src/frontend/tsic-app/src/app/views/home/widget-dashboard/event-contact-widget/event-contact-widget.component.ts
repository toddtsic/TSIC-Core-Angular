import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { WidgetDashboardService } from '../services/widget-dashboard.service';
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
		this.svc.getEventContact().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
