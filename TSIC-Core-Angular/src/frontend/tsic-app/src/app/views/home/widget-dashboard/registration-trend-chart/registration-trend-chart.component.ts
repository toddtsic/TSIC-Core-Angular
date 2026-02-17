import { Component, computed, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { ITooltipRenderEventArgs, IAxisLabelRenderEventArgs } from '@syncfusion/ej2-charts';
import type { RegistrationTimeSeriesDto } from '@core/api';

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

@Component({
	selector: 'app-registration-trend-chart',
	standalone: true,
	imports: [ChartAllModule],
	templateUrl: './registration-trend-chart.component.html',
	styleUrl: './registration-trend-chart.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationTrendChartComponent {
	readonly data = input.required<RegistrationTimeSeriesDto>();
	readonly loading = input(false);

	/** Label for the count badge (e.g., 'Registrations', 'Players', 'Teams') */
	readonly unitLabel = input('Registrations');

	/** Label for the daily bar series in the legend */
	readonly barSeriesName = input('Daily Regs');

	// Resolve palette colors eagerly so chart never receives post-init property changes
	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly accentColor = signal(cssVar('--brand-accent', '#6f42c1'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly surfaceBg = signal(cssVar('--brand-bg', '#ffffff'));
	readonly textColor = signal(cssVar('--brand-text', '#212529'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	// Chart data
	readonly chartData = computed(() => this.data().dailyData ?? []);

	// Primary axis config — extend to today so the chart never ends mid-season
	readonly primaryXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: 'MMM d',
		intervalType: 'Months' as const,
		interval: 1,
		maximum: new Date(),
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		edgeLabelPlacement: 'Shift' as const,
	}));

	readonly primaryYAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		minimum: 0,
	}));

	readonly tooltipSettings = {
		enable: true,
		shared: true,
		format: '${series.name}: <b>${point.y}</b>',
		header: '<b>${point.x}</b>',
	};

	readonly legendSettings = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px' },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};

	readonly chartArea = {
		border: { width: 0 },
	};

	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	onTooltipRender(args: ITooltipRenderEventArgs): void {
		// Format cumulative revenue in tooltip
		if (args.series?.name === 'Revenue') {
			const val = Number(args.point?.y ?? 0);
			args.text = `Revenue: <b>${new Intl.NumberFormat('en-US', {
				style: 'currency', currency: 'USD', maximumFractionDigits: 0
			}).format(val)}</b>`;
		}
	}

	onAxisLabelRender(args: IAxisLabelRenderEventArgs): void {
		// Format secondary Y axis labels as currency
		if (args.axis?.name === 'secondaryYAxis') {
			const val = Number(args.text?.replace(/,/g, '') ?? 0);
			if (val >= 1000) {
				args.text = `$${Math.round(val / 1000)}k`;
			} else {
				args.text = `$${val}`;
			}
		}
	}
}
