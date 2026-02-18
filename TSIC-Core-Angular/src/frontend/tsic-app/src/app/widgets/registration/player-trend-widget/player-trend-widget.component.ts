import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '@widgets/shared/chart-card/collapsible-chart-card.component';
import { RegistrationTrendChartComponent } from '@widgets/shared/registration-trend-chart/registration-trend-chart.component';
import type { RegistrationTimeSeriesDto } from '@core/api';

@Component({
	selector: 'app-player-trend-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, RegistrationTrendChartComponent],
	templateUrl: './player-trend-widget.component.html',
	styleUrl: './player-trend-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
	host: {
		'[class.widget-collapsed]': 'isCollapsed()',
		'[class.widget-expanded]': '!isCollapsed()',
	}
})
export class PlayerTrendWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<RegistrationTimeSeriesDto | null>(null);
	readonly hasError = signal(false);

	/** Two-way bound with collapsible-chart-card's collapsed model */
	readonly isCollapsed = signal(true);

	// KPI display values (moved up from registration-trend-chart)
	readonly totalRegsDisplay = computed(() => {
		const s = this.data()?.summary;
		return s ? s.totalRegistrations.toLocaleString() : '0';
	});

	readonly totalRevenueDisplay = computed(() => {
		const s = this.data()?.summary;
		if (!s) return '$0';
		return new Intl.NumberFormat('en-US', {
			style: 'currency', currency: 'USD', maximumFractionDigits: 0
		}).format(s.totalRevenue);
	});

	readonly outstandingDisplay = computed(() => {
		const s = this.data()?.summary;
		if (!s || s.totalOutstanding <= 0) return '';
		return new Intl.NumberFormat('en-US', {
			style: 'currency', currency: 'USD', maximumFractionDigits: 0
		}).format(s.totalOutstanding);
	});

	ngOnInit(): void {
		this.svc.getPlayerTrend().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
