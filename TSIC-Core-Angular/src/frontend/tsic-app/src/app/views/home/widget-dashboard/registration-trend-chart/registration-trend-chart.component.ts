import { Component, computed, input, signal, OnInit, AfterViewInit, ChangeDetectionStrategy, ElementRef, inject } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { ITooltipRenderEventArgs, IAxisLabelRenderEventArgs } from '@syncfusion/ej2-charts';
import type { RegistrationTimeSeriesDto, DailyRegistrationPointDto } from '@core/api';

@Component({
	selector: 'app-registration-trend-chart',
	standalone: true,
	imports: [ChartAllModule],
	templateUrl: './registration-trend-chart.component.html',
	styleUrl: './registration-trend-chart.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationTrendChartComponent implements AfterViewInit {
	private readonly el = inject(ElementRef);

	readonly data = input.required<RegistrationTimeSeriesDto>();
	readonly loading = input(false);

	/** Label for the count badge (e.g., 'Registrations', 'Players', 'Teams') */
	readonly unitLabel = input('Registrations');

	/** Label for the daily bar series in the legend */
	readonly barSeriesName = input('Daily Regs');

	// Resolved palette colors (read from CSS vars at runtime)
	primaryColor = signal('#0d6efd');
	accentColor = signal('#6f42c1');
	mutedColor = signal('#6c757d');
	surfaceBg = signal('#ffffff');
	textColor = signal('#212529');
	borderColor = signal('rgba(0,0,0,0.1)');

	// Chart configuration
	readonly chartData = computed(() => this.data().dailyData ?? []);
	readonly summary = computed(() => this.data().summary);

	// Summary display values
	readonly totalRegsDisplay = computed(() => {
		const s = this.summary();
		return s ? s.totalRegistrations.toLocaleString() : '0';
	});

	readonly totalRevenueDisplay = computed(() => {
		const s = this.summary();
		if (!s) return '$0';
		return new Intl.NumberFormat('en-US', {
			style: 'currency', currency: 'USD', maximumFractionDigits: 0
		}).format(s.totalRevenue);
	});

	readonly outstandingDisplay = computed(() => {
		const s = this.summary();
		if (!s || s.totalOutstanding <= 0) return '';
		return new Intl.NumberFormat('en-US', {
			style: 'currency', currency: 'USD', maximumFractionDigits: 0
		}).format(s.totalOutstanding);
	});

	// Primary axis config
	readonly primaryXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: 'MMM d',
		intervalType: 'Auto' as const,
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

	readonly secondaryYAxis = computed(() => ({
		title: '',
		opposedPosition: true,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.accentColor(), size: '11px' },
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

	ngAfterViewInit(): void {
		this.resolveColors();
	}

	/** Read CSS variable values from the DOM for chart theming */
	private resolveColors(): void {
		const root = document.documentElement;
		const cs = getComputedStyle(root);
		const read = (v: string, fallback: string) => cs.getPropertyValue(v)?.trim() || fallback;

		this.primaryColor.set(read('--bs-primary', '#0d6efd'));
		this.accentColor.set(read('--brand-accent', '#6f42c1'));
		this.mutedColor.set(read('--brand-text-muted', '#6c757d'));
		this.surfaceBg.set(read('--brand-bg', '#ffffff'));
		this.textColor.set(read('--brand-text', '#212529'));
		this.borderColor.set(read('--brand-border', 'rgba(0,0,0,0.1)'));
	}

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
