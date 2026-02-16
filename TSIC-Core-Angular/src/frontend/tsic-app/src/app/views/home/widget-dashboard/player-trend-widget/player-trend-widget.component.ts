import { Component, inject, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { WidgetDashboardService } from '../services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '../collapsible-chart-card/collapsible-chart-card.component';
import { RegistrationTrendChartComponent } from '../registration-trend-chart/registration-trend-chart.component';
import type { RegistrationTimeSeriesDto } from '@core/api';

@Component({
	selector: 'app-player-trend-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, RegistrationTrendChartComponent],
	template: `
		<app-collapsible-chart-card storageKey="player-trend" title="Player Registrations" icon="bi-people">
			@if (data(); as d) {
				<app-registration-trend-chart [data]="d" unitLabel="Players" barSeriesName="Daily Players" />
			} @else if (hasError()) {
				<div class="chart-error">Unable to load player trend</div>
			}
		</app-collapsible-chart-card>
	`,
	styles: [`.chart-error { padding: var(--space-4); color: var(--brand-text-muted); font-size: var(--font-size-sm); text-align: center; }`],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlayerTrendWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<RegistrationTimeSeriesDto | null>(null);
	readonly hasError = signal(false);

	ngOnInit(): void {
		this.svc.getPlayerTrend().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
