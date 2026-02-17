import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { ITextRenderEventArgs } from '@syncfusion/ej2-charts';

import { WidgetDashboardService } from '../services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '../collapsible-chart-card/collapsible-chart-card.component';
import type { AgegroupDistributionDto } from '@core/api';

const currencyFmt = new Intl.NumberFormat('en-US', {
	style: 'currency', currency: 'USD', maximumFractionDigits: 0,
});

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

@Component({
	selector: 'app-agegroup-distribution-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, ChartAllModule],
	templateUrl: './agegroup-distribution-widget.component.html',
	styleUrl: './agegroup-distribution-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
	host: {
		'[class.widget-collapsed]': 'isCollapsed()',
		'[class.widget-expanded]': '!isCollapsed()',
	}
})
export class AgegroupDistributionWidgetComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<AgegroupDistributionDto | null>(null);
	readonly hasError = signal(false);

	/** Two-way bound with collapsible-chart-card's collapsed model */
	readonly isCollapsed = signal(true);

	// Resolve palette colors eagerly so chart never receives post-init property changes
	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly accentColor = signal(cssVar('--brand-accent', '#6f42c1'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	readonly chartData = computed(() => this.data()?.agegroups ?? []);

	readonly totalPlayersDisplay = computed(() =>
		(this.data()?.totalPlayers ?? 0).toLocaleString());

	readonly totalTeamsDisplay = computed(() =>
		(this.data()?.totalTeams ?? 0).toLocaleString());

	readonly totalRevenueDisplay = computed(() =>
		currencyFmt.format(this.data()?.totalRevenue ?? 0));

	// Dynamic chart height based on number of age groups
	readonly chartHeight = computed(() => {
		const count = this.chartData().length;
		return `${Math.max(220, count * 40 + 60)}px`;
	});

	readonly primaryXAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
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

	/** Data label config for the Players series — shows revenue text at each bar end */
	readonly playerDataLabel = {
		visible: true,
		position: 'Top' as const,
		font: { size: '10px', color: '' },  // color set in textRender
	};

	readonly chartArea = { border: { width: 0 } };
	readonly margin = { left: 8, right: 8, top: 4, bottom: 4 };

	/** Replace default data label text with currency-formatted revenue */
	onTextRender(args: ITextRenderEventArgs): void {
		const points = this.chartData();
		const idx = args.point?.index ?? -1;
		if (idx >= 0 && idx < points.length) {
			const rev = points[idx].revenue;
			args.text = rev > 0 ? currencyFmt.format(rev) : '';
		}
		// Use muted text color for the label
		if (args.font) {
			args.font.color = this.mutedColor();
		}
	}

	ngOnInit(): void {
		this.svc.getAgegroupDistribution().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}

}
