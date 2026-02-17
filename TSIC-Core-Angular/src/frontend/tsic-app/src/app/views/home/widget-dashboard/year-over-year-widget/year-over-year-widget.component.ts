import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { ITooltipRenderEventArgs } from '@syncfusion/ej2-charts';

import { WidgetDashboardService } from '../services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '../collapsible-chart-card/collapsible-chart-card.component';
import type { YearOverYearComparisonDto, YearSeriesDto } from '@core/api';

/** Palette colors for prior-year series (current year uses --bs-primary) */
const PRIOR_COLORS = ['--brand-accent', '--bs-secondary', '--brand-text-muted'];

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

interface ChartPoint {
	syntheticDate: Date;
	cumulativeCount: number;
}

interface SeriesConfig {
	year: string;
	jobName: string;
	finalTotal: number;
	data: ChartPoint[];
	color: string;
	width: number;
	dashArray: string;
	isCurrent: boolean;
}

@Component({
	selector: 'app-year-over-year-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, ChartAllModule],
	templateUrl: './year-over-year-widget.component.html',
	styleUrl: './year-over-year-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
	host: {
		'[class.widget-collapsed]': 'isCollapsed()',
		'[class.widget-expanded]': '!isCollapsed()',
	}
})
export class YearOverYearWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly rawData = signal<YearOverYearComparisonDto | null>(null);
	readonly hasError = signal(false);
	readonly isCollapsed = signal(true);

	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	/** Prepared series configs for the chart */
	readonly seriesConfigs = computed((): SeriesConfig[] => {
		const data = this.rawData();
		if (!data || data.series.length === 0) return [];

		return data.series.map((s, idx) => {
			const isCurrent = s.year === data.currentYear;
			const colorVar = isCurrent ? '--bs-primary' : (PRIOR_COLORS[idx - 1] ?? '--brand-text-muted');
			return {
				year: s.year,
				jobName: s.jobName,
				finalTotal: s.finalTotal,
				data: s.dailyData.map(d => ({
					syntheticDate: this.toSyntheticDate(new Date(d.date)),
					cumulativeCount: d.cumulativeCount,
				})),
				color: cssVar(colorVar, isCurrent ? '#0d6efd' : '#6c757d'),
				width: isCurrent ? 3 : 1.5,
				dashArray: isCurrent ? '' : '5,3',
				isCurrent,
			};
		});
	});

	/** Current year's latest cumulative count */
	readonly currentCountDisplay = computed(() => {
		const data = this.rawData();
		if (!data) return '0';
		const current = data.series.find(s => s.year === data.currentYear);
		const count = current?.dailyData.at(-1)?.cumulativeCount ?? 0;
		return count.toLocaleString();
	});

	/** Pace vs. prior year: "+12% ahead" or "-8% behind" */
	readonly paceDisplay = computed(() => {
		const data = this.rawData();
		if (!data || data.series.length < 2) return '';

		const currentSeries = data.series.find(s => s.year === data.currentYear);
		const priorSeries = data.series.find(s => s.year !== data.currentYear);
		if (!currentSeries || !priorSeries) return '';

		const latestPoint = currentSeries.dailyData.at(-1);
		if (!latestPoint) return '';

		const currentCount = latestPoint.cumulativeCount;
		const latestDate = new Date(latestPoint.date);
		const targetMonth = latestDate.getMonth();
		const targetDay = latestDate.getDate();

		// Find prior year's count at the same month-day
		const priorAtSameDate = priorSeries.dailyData
			.filter(d => {
				const pd = new Date(d.date);
				return pd.getMonth() < targetMonth
					|| (pd.getMonth() === targetMonth && pd.getDate() <= targetDay);
			})
			.at(-1)?.cumulativeCount ?? 0;

		if (priorAtSameDate === 0) return '';

		const pct = ((currentCount - priorAtSameDate) / priorAtSameDate) * 100;
		const sign = pct >= 0 ? '+' : '';
		const label = pct >= 0 ? 'ahead' : 'behind';
		return `${sign}${Math.round(pct)}% ${label} of ${priorSeries.year}`;
	});

	/** Prior year's final total display */
	readonly priorFinalDisplay = computed(() => {
		const data = this.rawData();
		if (!data || data.series.length < 2) return '';
		const prior = data.series.find(s => s.year !== data.currentYear);
		if (!prior) return '';
		return `${prior.year}: ${prior.finalTotal.toLocaleString()} final`;
	});

	readonly hasData = computed(() => {
		const configs = this.seriesConfigs();
		return configs.length > 0;
	});

	readonly primaryXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: 'MMM d',
		intervalType: 'Months' as const,
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
	}));

	readonly primaryYAxis = computed(() => ({
		title: 'Registrations',
		titleStyle: { color: this.mutedColor(), size: '11px' },
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		minimum: 0,
	}));

	readonly tooltipSettings = {
		enable: true,
		shared: true,
	};

	readonly legendSettings = {
		visible: true,
		position: 'Bottom' as const,
		textStyle: { size: '11px' },
	};

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	/** Format tooltip to show real year + count */
	onTooltipRender(args: ITooltipRenderEventArgs): void {
		// The series name already contains the year — just format the value
		if (args.text) {
			args.text = args.text.replace(/<b>(\d+)<\/b>/, (_m: string, v: string) =>
				`<b>${parseInt(v, 10).toLocaleString()}</b>`);
		}
	}

	/**
	 * Normalize a real date to a synthetic shared year for X-axis alignment.
	 * Jul-Dec → 2000, Jan-Jun → 2001 (handles Nov→May season spans).
	 */
	private toSyntheticDate(realDate: Date): Date {
		const month = realDate.getMonth();
		const day = realDate.getDate();
		const syntheticYear = month >= 6 ? 2000 : 2001;
		return new Date(syntheticYear, month, day);
	}

	ngOnInit(): void {
		this.svc.getYearOverYear().subscribe({
			next: (d) => this.rawData.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
