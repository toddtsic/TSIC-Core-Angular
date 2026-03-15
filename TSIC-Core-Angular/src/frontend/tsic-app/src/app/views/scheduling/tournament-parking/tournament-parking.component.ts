import { Component, ChangeDetectionStrategy, signal, computed, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChartAllModule, ChartComponent } from '@syncfusion/ej2-angular-charts';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import {
	TournamentParkingService,
	type TournamentParkingResponse,
	type ParkingComplexDayDto,
	type ParkingTimeslotDto,
	type ParkingSummaryDto
} from './services/tournament-parking.service';

/** Read a CSS custom property from :root, with fallback. */
function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

@Component({
	selector: 'app-tournament-parking',
	standalone: true,
	imports: [FormsModule, ChartAllModule, GridAllModule],
	templateUrl: './tournament-parking.component.html',
	styleUrl: './tournament-parking.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class TournamentParkingComponent implements OnInit {
	private readonly parkingService = inject(TournamentParkingService);

	// ── UI State ──
	isLoading = signal(false);
	errorMessage = signal<string | null>(null);

	// ── Parameters ──
	arrivalBuffer = signal(45);
	departureBuffer = signal(30);
	carMultiplier = signal(24);

	// ── Data ──
	report = signal<TournamentParkingResponse | null>(null);
	activeTab = signal(0); // 0 = All Complexes, 1+ = per-complex/day

	// ── Derived ──
	complexDays = computed(() => this.report()?.complexDays ?? []);
	rollup = computed(() => this.report()?.rollup ?? []);
	summary = computed(() => this.report()?.summary ?? null);

	// ── Palette Colors ──
	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly successColor = signal(cssVar('--bs-success', '#198754'));
	readonly dangerColor = signal(cssVar('--brand-danger', '#dc3545'));
	readonly warningColor = signal(cssVar('--bs-warning', '#ffc107'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly textColor = signal(cssVar('--brand-text', '#212529'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	// ── Dropdown options ──
	readonly bufferOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60];
	readonly multiplierOptions = Array.from({ length: 31 }, (_, i) => i);

	// ── Chart config (computed for palette responsiveness) ──
	readonly carsChartPrimaryXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: 'h:mm a',
		intervalType: 'Minutes' as const,
		interval: 30,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		labelRotation: -45,
		edgeLabelPlacement: 'Shift' as const,
	}));

	readonly chartPrimaryYAxis = computed(() => ({
		title: '',
		minimum: 0,
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
	}));

	readonly chartTooltip = {
		enable: true,
		shared: true,
		format: '${series.name}: <b>${point.y}</b>',
		header: '<b>${point.x}</b>',
	};

	readonly chartLegend = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px' },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};

	readonly chartArea = { border: { width: 0 } };
	readonly chartMargin = { left: 8, right: 8, top: 4, bottom: 4 };

	// ── Grid columns ──
	readonly gridColumns = [
		{ field: 'fieldComplex', headerText: 'Complex', width: 100 },
		{ field: 'day', headerText: 'Day', width: 90, format: { type: 'date', format: 'MM/dd' } },
		{ field: 'time', headerText: 'Time', width: 80, format: { type: 'date', format: 'h:mm a' } },
		{ field: 'teamsArriving', headerText: 'Teams+', textAlign: 'Right', width: 70, format: 'N0' },
		{ field: 'teamsDeparting', headerText: 'Teams-', textAlign: 'Right', width: 70, format: 'N0' },
		{ field: 'teamsNet', headerText: 'Net', textAlign: 'Right', width: 60, format: 'N0' },
		{ field: 'teamsOnSite', headerText: 'On-Site', textAlign: 'Right', width: 75, format: 'N0' },
		{ field: 'carsArriving', headerText: 'Cars+', textAlign: 'Right', width: 65, format: 'N0' },
		{ field: 'carsDeparting', headerText: 'Cars-', textAlign: 'Right', width: 65, format: 'N0' },
		{ field: 'carsNet', headerText: 'Net', textAlign: 'Right', width: 60, format: 'N0' },
		{ field: 'carsOnSite', headerText: 'On-Site', textAlign: 'Right', width: 75, format: 'N0' },
	];

	ngOnInit(): void {
		this.loadReport();
	}

	loadReport(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.parkingService.getReport({
			arrivalBufferMinutes: this.arrivalBuffer(),
			departureBufferMinutes: this.departureBuffer(),
			carMultiplier: this.carMultiplier()
		}).subscribe({
			next: (data) => {
				this.report.set(data);
				this.isLoading.set(false);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message || 'Failed to load parking report');
				this.isLoading.set(false);
			}
		});
	}

	onParameterChange(): void {
		this.loadReport();
	}

	setActiveTab(index: number): void {
		this.activeTab.set(index);
	}

	/** Get the active complex-day data for the current tab (tab index 1+ maps to complexDays[i-1]). */
	activeComplexDay = computed<ParkingComplexDayDto | null>(() => {
		const idx = this.activeTab();
		if (idx === 0) return null;
		return this.complexDays()[idx - 1] ?? null;
	});

	/** Convert timeslot date strings to Date objects for Syncfusion chart. */
	chartData = computed(() => {
		const cd = this.activeComplexDay();
		if (!cd) return [];
		return cd.timeslots.map(t => ({
			...t,
			time: new Date(t.time),
		}));
	});

	onGridToolbarClick(args: any, grid: GridComponent): void {
		if (args.item?.id?.endsWith('_excelexport')) {
			grid.excelExport({ fileName: 'TournamentParking.xlsx' });
		}
	}

	exportChart(chart: ChartComponent, name: string): void {
		chart.export('PNG', name);
	}

	printChart(chart: ChartComponent): void {
		chart.print();
	}

	formatNumber(n: number): string {
		return new Intl.NumberFormat('en-US').format(n);
	}
}
