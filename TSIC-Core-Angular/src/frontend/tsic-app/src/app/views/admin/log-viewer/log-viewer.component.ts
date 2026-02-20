import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChartAllModule } from '@syncfusion/ej2-angular-charts';
import { AccumulationChartAllModule } from '@syncfusion/ej2-angular-charts';
import { LogViewerService } from './log-viewer.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { LogEntryDto, LogStatsDto, LogCountByHour, TopErrorDto } from './log-viewer.service';

function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

// ── Time range presets for dashboard ──
interface TimeRange {
	label: string;
	hours: number;
}

const TIME_RANGES: TimeRange[] = [
	{ label: 'Last Hour', hours: 1 },
	{ label: 'Last 6h', hours: 6 },
	{ label: 'Last 24h', hours: 24 },
	{ label: 'Last 7d', hours: 168 },
];

const LEVEL_OPTIONS = ['', 'Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'];

@Component({
	selector: 'app-log-viewer',
	standalone: true,
	imports: [CommonModule, FormsModule, ChartAllModule, AccumulationChartAllModule, TsicDialogComponent],
	templateUrl: './log-viewer.component.html',
	styleUrl: './log-viewer.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class LogViewerComponent implements OnInit {
	private readonly logService = inject(LogViewerService);
	private readonly toast = inject(ToastService);

	// ── Tab state ──
	readonly activeTab = signal<'explorer' | 'dashboard'>('dashboard');

	// ── Explorer state ──
	readonly logs = signal<LogEntryDto[]>([]);
	readonly totalCount = signal(0);
	readonly page = signal(1);
	readonly pageSize = signal(50);
	readonly isLoading = signal(false);
	readonly expandedId = signal<number | null>(null);

	// Filters
	readonly filterLevel = signal('');
	readonly filterSearch = signal('');
	readonly filterFrom = signal('');
	readonly filterTo = signal('');

	readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
	readonly pageNumbers = computed(() => {
		const total = this.totalPages();
		const current = this.page();
		const maxVisible = 7;
		let start = Math.max(1, current - Math.floor(maxVisible / 2));
		const end = Math.min(total, start + maxVisible - 1);
		start = Math.max(1, end - maxVisible + 1);
		const pages: number[] = [];
		for (let i = start; i <= end; i++) pages.push(i);
		return pages;
	});
	readonly levelOptions = LEVEL_OPTIONS;

	// ── Dashboard state ──
	readonly stats = signal<LogStatsDto | null>(null);
	readonly statsLoading = signal(false);
	readonly selectedRange = signal<TimeRange>(TIME_RANGES[2]); // Default 24h
	readonly timeRanges = TIME_RANGES;

	// ── Purge dialog ──
	readonly showPurgeDialog = signal(false);
	readonly purging = signal(false);
	readonly purgeDays = signal(30);

	// ── Chart colors (palette-aware) ──
	readonly primaryColor = signal(cssVar('--bs-primary', '#0d6efd'));
	readonly successColor = signal(cssVar('--bs-success', '#198754'));
	readonly warningColor = signal(cssVar('--bs-warning', '#ffc107'));
	readonly dangerColor = signal(cssVar('--bs-danger', '#dc3545'));
	readonly infoColor = signal(cssVar('--bs-info', '#0dcaf0'));
	readonly mutedColor = signal(cssVar('--brand-text-muted', '#6c757d'));
	readonly borderColor = signal(cssVar('--brand-border', 'rgba(0,0,0,0.1)'));

	// ── Chart series (property-binding approach — avoids content projection timing) ──
	private readonly statusRangeOrder = ['2XX', '3XX', '4XX', '5XX'];

	readonly statusSeries = computed(() => {
		const raw = this.stats()?.countsByHourByStatus ?? [];
		const ranges = this.statusRangeOrder.filter(r => raw.some(d => d.statusRange === r));
		return ranges.map(range => ({
			dataSource: raw.filter(r => r.statusRange === range).map(r => ({
				x: new Date(r.hour), y: r.count,
			})),
			type: 'StackingColumn',
			xName: 'x', yName: 'y',
			name: range,
			fill: this.statusRangeColor(range),
			columnWidth: 0.7,
		}));
	});

	readonly hourlySeries = computed(() => {
		const raw = this.stats()?.countsByHour ?? [];
		const levels = [...new Set(raw.map(r => r.level))];
		return levels.map(level => ({
			dataSource: raw.filter(r => r.level === level).map(r => ({
				x: new Date(r.hour), y: r.count,
			})),
			type: 'Line',
			xName: 'x', yName: 'y',
			name: level,
			fill: this.levelColor(level),
			width: 2,
			marker: { visible: true, width: 6, height: 6 },
		}));
	});

	readonly statusXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: this.selectedRange().hours <= 24 ? 'HH:mm' : 'MMM dd',
		intervalType: 'Hours' as const,
		majorGridLines: { width: 0 },
		lineStyle: { color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px' },
	}));

	readonly statusYAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		minimum: 0,
	}));

	readonly levelDistribution = computed(() => {
		const byLevel = this.stats()?.countsByLevel ?? {};
		return Object.entries(byLevel).map(([level, count]) => ({
			x: level,
			y: count,
			fill: this.levelColor(level),
		}));
	});

	readonly topErrors = computed<TopErrorDto[]>(() => this.stats()?.topErrors ?? []);
	readonly statsTotalCount = computed(() => this.stats()?.totalCount ?? 0);

	readonly hourlyXAxis = computed(() => ({
		valueType: 'DateTime' as const,
		labelFormat: 'HH:mm',
		intervalType: 'Hours' as const,
		majorGridLines: { width: 0 },
		lineStyle: { color: this.borderColor() },
		labelStyle: { color: this.mutedColor(), size: '11px' },
	}));

	readonly hourlyYAxis = computed(() => ({
		title: '',
		majorGridLines: { width: 0.5, color: this.borderColor(), dashArray: '3,3' },
		lineStyle: { width: 0 },
		labelStyle: { color: this.mutedColor(), size: '11px' },
		minimum: 0,
	}));

	ngOnInit(): void {
		this.loadStats();
	}

	// ── Explorer actions ──

	loadLogs(): void {
		this.isLoading.set(true);
		this.logService.query({
			level: this.filterLevel() || undefined,
			search: this.filterSearch() || undefined,
			from: this.filterFrom() || undefined,
			to: this.filterTo() || undefined,
			page: this.page(),
			pageSize: this.pageSize(),
		}).subscribe({
			next: (res) => {
				this.logs.set(res.items);
				this.totalCount.set(res.totalCount);
				this.isLoading.set(false);
			},
			error: () => {
				this.toast.show('Failed to load logs', 'danger');
				this.isLoading.set(false);
			},
		});
	}

	applyFilters(): void {
		this.page.set(1);
		this.loadLogs();
	}

	clearFilters(): void {
		this.filterLevel.set('');
		this.filterSearch.set('');
		this.filterFrom.set('');
		this.filterTo.set('');
		this.page.set(1);
		this.loadLogs();
	}

	goToPage(p: number): void {
		if (p < 1 || p > this.totalPages()) return;
		this.page.set(p);
		this.loadLogs();
	}

	toggleRow(id: number): void {
		this.expandedId.set(this.expandedId() === id ? null : id);
	}

	// ── Dashboard actions ──

	private explorerLoaded = false;

	switchTab(tab: 'explorer' | 'dashboard'): void {
		this.activeTab.set(tab);
		if (tab === 'dashboard' && !this.stats()) {
			this.loadStats();
		}
		if (tab === 'explorer' && !this.explorerLoaded) {
			this.explorerLoaded = true;
			this.loadLogs();
		}
	}

	selectTimeRange(range: TimeRange): void {
		this.selectedRange.set(range);
		this.loadStats();
	}

	loadStats(): void {
		this.statsLoading.set(true);
		const now = new Date();
		const from = new Date(now.getTime() - this.selectedRange().hours * 3600000);
		this.logService.getStats(from.toISOString(), now.toISOString()).subscribe({
			next: (data) => {
				this.stats.set(data);
				this.statsLoading.set(false);
			},
			error: () => {
				this.toast.show('Failed to load log stats', 'danger');
				this.statsLoading.set(false);
			},
		});
	}

	// ── Purge ──

	openPurgeDialog(): void {
		this.showPurgeDialog.set(true);
	}

	closePurgeDialog(): void {
		this.showPurgeDialog.set(false);
	}

	confirmPurge(): void {
		this.purging.set(true);
		this.logService.purge(this.purgeDays()).subscribe({
			next: (res) => {
				this.toast.show(`Purged ${res.deletedCount.toLocaleString()} log entries`, 'success');
				this.purging.set(false);
				this.showPurgeDialog.set(false);
				this.loadLogs();
				if (this.stats()) this.loadStats();
			},
			error: () => {
				this.toast.show('Purge failed', 'danger');
				this.purging.set(false);
			},
		});
	}

	// ── Helpers ──

	levelColor(level: string): string {
		switch (level) {
			case 'Fatal':
			case 'Error': return this.dangerColor();
			case 'Warning': return this.warningColor();
			case 'Information': return this.infoColor();
			case 'Debug': return this.successColor();
			default: return this.mutedColor();
		}
	}

	levelBadgeClass(level: string): string {
		switch (level) {
			case 'Fatal':
			case 'Error': return 'badge-error';
			case 'Warning': return 'badge-warning';
			case 'Information': return 'badge-info';
			case 'Debug': return 'badge-debug';
			default: return 'badge-verbose';
		}
	}

	formatTimestamp(iso: string): string {
		const d = new Date(iso);
		return d.toLocaleString('en-US', {
			month: 'short', day: 'numeric',
			hour: '2-digit', minute: '2-digit', second: '2-digit',
			hour12: false,
		});
	}

	formatElapsed(ms: number | null): string {
		if (ms == null) return '-';
		if (ms < 1) return '<1ms';
		if (ms < 1000) return `${Math.round(ms)}ms`;
		return `${(ms / 1000).toFixed(2)}s`;
	}

	truncateSource(ctx: string | null): string {
		if (!ctx) return '-';
		const parts = ctx.split('.');
		return parts[parts.length - 1];
	}

	statusRangeColor(range: string): string {
		switch (range) {
			case '2XX': return this.successColor();
			case '3XX': return this.infoColor();
			case '4XX': return this.warningColor();
			case '5XX': return this.dangerColor();
			default: return this.mutedColor();
		}
	}

	statusBadgeClass(code: number | null): string {
		if (code == null) return '';
		if (code >= 500) return 'status-5xx';
		if (code >= 400) return 'status-4xx';
		if (code >= 300) return 'status-3xx';
		return 'status-2xx';
	}
}
