import { Component, inject, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { WidgetDashboardService } from '../services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '../collapsible-chart-card/collapsible-chart-card.component';
import { RegistrationTrendChartComponent } from '../registration-trend-chart/registration-trend-chart.component';
import type { RegistrationTimeSeriesDto } from '@core/api';

@Component({
	selector: 'app-team-trend-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, RegistrationTrendChartComponent],
	template: `
		<app-collapsible-chart-card storageKey="team-trend" title="Team Registrations" icon="bi-shield">
			@if (data(); as d) {
				<app-registration-trend-chart [data]="d" unitLabel="Teams" barSeriesName="Daily Teams" />
			} @else if (hasError()) {
				<div class="chart-error">Unable to load team trend</div>
			}
		</app-collapsible-chart-card>
	`,
	styles: [`.chart-error { padding: var(--space-4); color: var(--brand-text-muted); font-size: var(--font-size-sm); text-align: center; }`],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamTrendWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<RegistrationTimeSeriesDto | null>(null);
	readonly hasError = signal(false);

	ngOnInit(): void {
		this.svc.getTeamTrend().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
