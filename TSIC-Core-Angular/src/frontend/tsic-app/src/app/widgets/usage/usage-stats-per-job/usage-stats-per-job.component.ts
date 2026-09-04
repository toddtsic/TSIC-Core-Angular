import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';

import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import type { UsageStatsPerJobDto } from '@core/api';

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

/** Windows offered by the selector. Server clamps to 1–365 regardless. */
const WINDOWS = [
	{ days: 1, label: '24h' },
	{ days: 7, label: '7d' },
	{ days: 30, label: '30d' },
] as const;

@Component({
	selector: 'app-usage-stats-per-job',
	standalone: true,
	imports: [ChartAllModule],
	templateUrl: './usage-stats-per-job.component.html',
	styleUrl: './usage-stats-per-job.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageStatsPerJobComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<UsageStatsPerJobDto | null>(null);
	readonly hasError = signal(false);
	readonly isLoading = signal(false);

	/** The chart is the whole point of this card, so it opens with it showing.
	    The toggle survives for a reader who wants the summary line alone. */
	readonly isChartOpen = signal(true);

	readonly windows = WINDOWS;
	readonly windowDays = signal<number>(7);
	readonly excludeBots = signal(true);

	// Resolved eagerly so the chart never receives post-init property changes.
	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly accentColor = signal(cssVar('--brand-accent', '#6f42c1'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));
	// The gap colour between stacked segments -- it must be the CARD colour, not the
	// page colour, or the seam reads as a dark line through every bar.
	readonly surfaceColor = signal(cssVar('--brand-surface', '#ffffff'));

	readonly chartData = computed(() => this.data()?.rows ?? []);

	/**
	 * TSICLogs not configured on this server. Distinct from "no traffic" — an empty
	 * chart would read as nobody having used anything, which is a worse answer than
	 * saying the data source is missing.
	 */
	readonly isUnavailable = computed(() =>
		this.data() !== null && !this.data()!.usageLoggingAvailable);

	readonly hasNoTraffic = computed(() =>
		this.data() !== null
		&& this.data()!.usageLoggingAvailable
		&& this.chartData().length === 0);

	readonly totalRequestsDisplay = computed(() =>
		(this.data()?.totalRequests ?? 0).toLocaleString());

	readonly totalJobsDisplay = computed(() =>
		(this.data()?.totalJobs ?? 0).toLocaleString());

	/**
	 * What the chart is NOT showing. A truncated chart with no statement of what was
	 * truncated is how a reader concludes the total is smaller than it is.
	 */
	readonly remainderNote = computed(() => {
		const d = this.data();
		if (!d || d.otherJobCount === 0) return '';
		return `plus ${d.otherJobCount.toLocaleString()} more `
			+ `${d.otherJobCount === 1 ? 'event' : 'events'} `
			+ `(${d.otherRequests.toLocaleString()} requests) not charted`;
	});

	/**
	 * Bar thickness in PIXELS, not a fraction of the category slot.
	 *
	 * ej2 sizes a proportional `columnWidth` against the slot, and the slot is chart
	 * height divided by category count — so one event drew a single enormous bar and
	 * twelve drew thin ones. Thickness carries no information here; bar LENGTH does.
	 * Pinning it keeps the chart reading the same at every row count and across a
	 * 24h/7d/30d switch that changes how many events qualify.
	 *
	 * The property is `columnWidthInPixel`, SINGULAR. `columnWidthInPixels` does not
	 * exist in ej2 33.x and fails silently — unknown series properties are ignored.
	 */
	readonly barThickness = 18;

	/**
	 * One slot per event, sized to the bar plus its gap, so rows stay evenly spaced
	 * however many there are. The floor covers the chrome (legend, axis labels,
	 * margins) without stranding a single-row chart in whitespace.
	 */
	readonly chartHeight = computed(() => {
		const count = this.chartData().length;
		return `${Math.max(130, count * 34 + 70)}px`;
	});

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		labelIntersectAction: 'None' as const,
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
	};

	readonly legendSettings = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px' },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	toggleChart(): void {
		this.isChartOpen.set(!this.isChartOpen());
	}

	setWindow(days: number): void {
		if (days === this.windowDays()) return;
		this.windowDays.set(days);
		this.load();
	}

	toggleBots(): void {
		this.excludeBots.set(!this.excludeBots());
		this.load();
	}

	/**
	 * Explicit callback, never an effect(): the load is an action taken in response to
	 * a user choice, not a derivation of state.
	 */
	load(): void {
		this.isLoading.set(true);
		this.hasError.set(false);

		this.svc.getUsageStatsPerJob(this.windowDays(), this.excludeBots()).subscribe({
			next: (d) => {
				this.data.set(d);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			},
		});
	}

	ngOnInit(): void {
		this.load();
	}
}
