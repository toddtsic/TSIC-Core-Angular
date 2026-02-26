import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { ITooltipRenderEventArgs } from '@syncfusion/ej2-charts';

import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '@widgets/shared/chart-card/collapsible-chart-card.component';
import type { YearOverYearComparisonDto, YearSeriesDto } from '@core/api';

/** Palette colors for prior-year series (current year uses --bs-primary) */
const PRIOR_COLORS = ['--brand-accent', '--bs-secondary', '--brand-text-muted'];

/** Short month labels for X-axis */
const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

interface MonthlyBarPoint {
	month: string;
	count: number;
}

interface BarSeriesConfig {
	year: string;
	jobName: string;
	finalTotal: number;
	data: MonthlyBarPoint[];
	color: string;
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

	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	/** Grouped bar series configs — one per year */
	readonly barSeriesConfigs = computed((): BarSeriesConfig[] => {
		const data = this.rawData();
		if (!data || data.series.length === 0) return [];

		// Collect all months across all series for consistent X-axis
		const allMonths = this.getAllMonthLabels(data.series);

		return data.series.map((s, idx) => {
			const isCurrent = s.year === data.currentYear;
			const colorVar = isCurrent ? '--bs-primary' : (PRIOR_COLORS[idx - 1] ?? '--brand-text-muted');
			const monthlyData = this.toMonthlyNewCounts(s, allMonths);

			return {
				year: s.year,
				jobName: s.jobName,
				finalTotal: s.finalTotal,
				data: monthlyData,
				color: cssVar(colorVar, isCurrent ? '#0d6efd' : '#6c757d'),
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

	readonly hasData = computed(() => this.barSeriesConfigs().length > 0);

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0.5, color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px' },
	}));

	readonly primaryYAxis = computed(() => ({
		title: 'New Registrations',
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

	/** Format tooltip to show year + count */
	onTooltipRender(args: ITooltipRenderEventArgs): void {
		if (args.text) {
			args.text = args.text.replace(/<b>(\d+)<\/b>/, (_m: string, v: string) =>
				`<b>${parseInt(v, 10).toLocaleString()}</b>`);
		}
	}

	/**
	 * Convert daily cumulative data to monthly net-new registration counts.
	 * Uses the difference between the last cumulative value of each month
	 * and the previous month's last cumulative value.
	 */
	private toMonthlyNewCounts(series: YearSeriesDto, allMonths: string[]): MonthlyBarPoint[] {
		// Group daily data by month index
		const monthBuckets = new Map<number, number>();
		for (const d of series.dailyData) {
			const date = new Date(d.date);
			const monthIdx = date.getMonth();
			// Keep the max cumulative per month (last day of that month's data)
			const existing = monthBuckets.get(monthIdx);
			if (existing === undefined || d.cumulativeCount > existing) {
				monthBuckets.set(monthIdx, d.cumulativeCount);
			}
		}

		// Convert cumulative → net new per month
		const sortedMonths = [...monthBuckets.entries()].sort((a, b) => {
			// Sort by season order (Jul-Dec first, then Jan-Jun)
			const orderA = a[0] >= 6 ? a[0] - 6 : a[0] + 6;
			const orderB = b[0] >= 6 ? b[0] - 6 : b[0] + 6;
			return orderA - orderB;
		});

		const netNewByMonth = new Map<string, number>();
		let prevCumulative = 0;
		for (const [monthIdx, cumulative] of sortedMonths) {
			const label = MONTH_LABELS[monthIdx];
			netNewByMonth.set(label, cumulative - prevCumulative);
			prevCumulative = cumulative;
		}

		// Return data aligned to allMonths (0 for missing months)
		return allMonths.map(month => ({
			month,
			count: netNewByMonth.get(month) ?? 0,
		}));
	}

	/**
	 * Collect all unique month labels across all series in season order.
	 * (Jul-Dec first, then Jan-Jun to handle cross-year seasons)
	 */
	private getAllMonthLabels(series: YearSeriesDto[]): string[] {
		const monthSet = new Set<number>();
		for (const s of series) {
			for (const d of s.dailyData) {
				monthSet.add(new Date(d.date).getMonth());
			}
		}

		return [...monthSet]
			.sort((a, b) => {
				const orderA = a >= 6 ? a - 6 : a + 6;
				const orderB = b >= 6 ? b - 6 : b + 6;
				return orderA - orderB;
			})
			.map(m => MONTH_LABELS[m]);
	}

	ngOnInit(): void {
		this.svc.getYearOverYear().subscribe({
			next: (d) => this.rawData.set(d),
			error: () => this.hasError.set(true),
		});
	}
}
