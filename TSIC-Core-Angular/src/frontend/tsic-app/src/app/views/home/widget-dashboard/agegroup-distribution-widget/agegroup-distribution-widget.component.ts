import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit, AfterViewInit } from '@angular/core';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import type { IAxisLabelRenderEventArgs } from '@syncfusion/ej2-charts';
import { WidgetDashboardService } from '../services/widget-dashboard.service';
import { CollapsibleChartCardComponent } from '../collapsible-chart-card/collapsible-chart-card.component';
import type { AgegroupDistributionDto } from '@core/api';

@Component({
	selector: 'app-agegroup-distribution-widget',
	standalone: true,
	imports: [CollapsibleChartCardComponent, ChartAllModule],
	templateUrl: './agegroup-distribution-widget.component.html',
	styleUrl: './agegroup-distribution-widget.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class AgegroupDistributionWidgetComponent implements OnInit, AfterViewInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<AgegroupDistributionDto | null>(null);
	readonly hasError = signal(false);

	// Palette colors
	primaryColor = signal('#0d6efd');
	accentColor = signal('#6f42c1');
	mutedColor = signal('#6c757d');
	borderColor = signal('rgba(0,0,0,0.1)');

	readonly chartData = computed(() => this.data()?.agegroups ?? []);

	readonly totalPlayersDisplay = computed(() =>
		(this.data()?.totalPlayers ?? 0).toLocaleString());

	readonly totalTeamsDisplay = computed(() =>
		(this.data()?.totalTeams ?? 0).toLocaleString());

	// Dynamic chart height based on number of age groups
	readonly chartHeight = computed(() => {
		const count = this.chartData().length;
		return `${Math.max(120, count * 32 + 40)}px`;
	});

	readonly primaryXAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		minimum: 0,
	}));

	readonly primaryYAxis = computed(() => ({
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
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

	ngOnInit(): void {
		this.svc.getAgegroupDistribution().subscribe({
			next: (d) => this.data.set(d),
			error: () => this.hasError.set(true),
		});
	}

	ngAfterViewInit(): void {
		this.resolveColors();
	}

	private resolveColors(): void {
		const cs = getComputedStyle(document.documentElement);
		const read = (v: string, fallback: string) => cs.getPropertyValue(v)?.trim() || fallback;

		this.primaryColor.set(read('--bs-primary', '#0d6efd'));
		this.accentColor.set(read('--brand-accent', '#6f42c1'));
		this.mutedColor.set(read('--brand-text-muted', '#6c757d'));
		this.borderColor.set(read('--brand-border', 'rgba(0,0,0,0.1)'));
	}
}
