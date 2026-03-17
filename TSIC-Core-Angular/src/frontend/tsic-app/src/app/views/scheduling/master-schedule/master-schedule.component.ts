import {
	Component, ChangeDetectionStrategy, inject, signal, computed,
	OnInit, OnDestroy, ViewChild, ElementRef, NgZone,
} from '@angular/core';
import type { MasterScheduleResponse, MasterScheduleDay } from '@core/api';
import { MasterScheduleService } from './services/master-schedule.service';

@Component({
	selector: 'app-master-schedule',
	standalone: true,
	templateUrl: './master-schedule.component.html',
	styleUrl: './master-schedule.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterScheduleComponent implements OnInit, OnDestroy {
	private readonly svc = inject(MasterScheduleService);
	private readonly zone = inject(NgZone);

	@ViewChild('msGrid') msGridRef?: ElementRef<HTMLElement>;
	@ViewChild('minimapCanvas') minimapCanvasRef?: ElementRef<HTMLCanvasElement>;

	readonly masterData = signal<MasterScheduleResponse | null>(null);
	readonly isLoading = signal(true);
	readonly hasError = signal(false);
	readonly isExporting = signal(false);
	readonly activeDayIndex = signal(0);

	readonly activeDay = computed((): MasterScheduleDay | null => {
		const data = this.masterData();
		if (!data || data.days.length === 0) return null;
		return data.days[this.activeDayIndex()] ?? null;
	});

	readonly totalGames = computed(() => this.masterData()?.totalGames ?? 0);
	readonly fieldCount = computed(() => this.masterData()?.fieldColumns.length ?? 0);

	readonly gridTemplateColumns = computed(() => {
		const cols = this.masterData()?.fieldColumns.length ?? 0;
		return `minmax(80px, auto) repeat(${cols}, minmax(140px, 1fr))`;
	});

	ngOnInit(): void {
		this.svc.getMasterSchedule().subscribe({
			next: (data) => {
				this.masterData.set(data);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			},
		});
		document.addEventListener('keydown', this.onKeyDown);
	}

	selectDay(index: number): void {
		this.activeDayIndex.set(index);
	}

	exportDay(): void {
		this.isExporting.set(true);
		const day = this.activeDay();
		this.svc.exportExcel(false, this.activeDayIndex()).subscribe({
			next: (blob) => {
				this.downloadBlob(blob,
					`MasterSchedule-${day?.shortLabel ?? 'Day'}.xlsx`);
				this.isExporting.set(false);
			},
			error: () => this.isExporting.set(false),
		});
	}

	exportAll(): void {
		this.isExporting.set(true);
		this.svc.exportExcel(false).subscribe({
			next: (blob) => {
				this.downloadBlob(blob, 'MasterSchedule-Full.xlsx');
				this.isExporting.set(false);
			},
			error: () => this.isExporting.set(false),
		});
	}

	private downloadBlob(blob: Blob, fileName: string): void {
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = fileName;
		a.click();
		URL.revokeObjectURL(url);
	}

	// ══════════════════════════════════════════════════════════════
	// Minimap — bird's-eye grid navigator (shared pattern with schedule-grid)
	// ══════════════════════════════════════════════════════════════

	readonly minimapOpen = signal(false);
	private isMinimapDragging = false;
	private scrollHandler?: () => void;
	private scrollContainer?: HTMLElement; // <main> with overflow-y:auto

	private readonly onKeyDown = (e: KeyboardEvent) => {
		if (e.key === 'Escape' && this.minimapOpen()) {
			this.zone.run(() => this.closeMinimap());
		}
		if (e.key === 'm' || e.key === 'M') {
			const el = e.target as HTMLElement;
			const tag = el?.tagName;
			if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
			if (el?.isContentEditable) return;
			this.zone.run(() => this.toggleMinimap());
		}
	};

	toggleMinimap(): void {
		if (this.minimapOpen()) {
			this.closeMinimap();
		} else {
			this.minimapOpen.set(true);
			setTimeout(() => this.openMinimap());
		}
	}

	private getScrollContainer(): HTMLElement | undefined {
		if (!this.scrollContainer) {
			// Walk up from the grid to find the nearest ancestor with vertical overflow.
			// The hub layout uses .hub-content (overflow-y: auto), NOT <main>.
			let node = this.msGridRef?.nativeElement.parentElement;
			while (node && node !== document.documentElement) {
				const ov = getComputedStyle(node).overflowY;
				if (ov === 'auto' || ov === 'scroll') {
					this.scrollContainer = node;
					break;
				}
				node = node.parentElement;
			}
		}
		return this.scrollContainer;
	}

	private openMinimap(): void {
		this.renderMinimap();
		const el = this.msGridRef?.nativeElement;
		const main = this.getScrollContainer();
		this.scrollHandler = () => this.renderMinimap();
		// Grid scrolls horizontally; <main> scrolls vertically
		if (el) {
			el.addEventListener('scroll', this.scrollHandler, { passive: true });
		}
		if (main) {
			main.addEventListener('scroll', this.scrollHandler, { passive: true });
		}
	}

	private closeMinimap(): void {
		this.minimapOpen.set(false);
		if (this.scrollHandler) {
			this.msGridRef?.nativeElement.removeEventListener('scroll', this.scrollHandler);
			this.getScrollContainer()?.removeEventListener('scroll', this.scrollHandler);
			this.scrollHandler = undefined;
		}
	}

	renderMinimap(): void {
		const canvas = this.minimapCanvasRef?.nativeElement;
		const el = this.msGridRef?.nativeElement;
		const day = this.activeDay();
		if (!canvas || !el || !day) return;

		const gridW = el.scrollWidth;
		const gridH = el.scrollHeight;

		// Scale to fit in max 260x180 bounding box
		const maxW = 260, maxH = 180;
		const scale = Math.min(maxW / gridW, maxH / gridH);
		canvas.width = Math.round(gridW * scale);
		canvas.height = Math.round(gridH * scale);

		const ctx = canvas.getContext('2d')!;
		const style = getComputedStyle(document.documentElement);

		// Background
		ctx.fillStyle = style.getPropertyValue('--bs-tertiary-bg').trim() || '#f5f5f4';
		ctx.fillRect(0, 0, canvas.width, canvas.height);

		// Draw game cells from active day data
		const cols = this.masterData()?.fieldColumns ?? [];
		const rows = day.rows;
		if (rows.length === 0 || cols.length === 0) return;

		const timeColW = 80 * scale;
		const fieldColW = (canvas.width - timeColW) / cols.length;
		// +1 for header row
		const rowH = canvas.height / (rows.length + 1);
		const colorFallback = style.getPropertyValue('--bs-primary').trim() || '#0d6efd';

		// Header row
		ctx.fillStyle = style.getPropertyValue('--bg-elevated').trim() || '#e7e5e4';
		ctx.fillRect(0, 0, canvas.width, rowH);

		// Data rows
		for (let ri = 0; ri < rows.length; ri++) {
			const cells = rows[ri].cells;
			for (let ci = 0; ci < cells.length; ci++) {
				const cell = cells[ci];
				if (!cell) continue;

				ctx.fillStyle = cell.color || colorFallback;
				const x = timeColW + ci * fieldColW + 0.5;
				const y = (ri + 1) * rowH + 0.5;
				ctx.fillRect(x, y, fieldColW - 1, rowH - 1);
			}
		}

		// Viewport rectangle — horizontal from grid scroll, vertical from <main> scroll
		const main = this.getScrollContainer();
		const vpX = el.scrollLeft * scale;
		// How far the grid top has scrolled above <main>'s visible top edge
		const mainTop = main?.getBoundingClientRect().top ?? 0;
		const gridTop = el.getBoundingClientRect().top;
		const vpY = Math.max(0, mainTop - gridTop) * scale;
		const vpW = el.clientWidth * scale;
		const vpH = (main?.clientHeight ?? window.innerHeight) * scale;

		ctx.fillStyle = 'rgba(13, 110, 253, 0.1)';
		ctx.fillRect(vpX, vpY, vpW, vpH);
		ctx.strokeStyle = colorFallback;
		ctx.lineWidth = 2;
		ctx.strokeRect(vpX, vpY, vpW, vpH);
	}

	onMinimapDown(e: MouseEvent): void {
		this.isMinimapDragging = true;
		this.scrollFromMinimap(e);
		e.preventDefault();
	}

	onMinimapMove(e: MouseEvent): void {
		if (!this.isMinimapDragging) return;
		this.scrollFromMinimap(e);
	}

	onMinimapUp(): void {
		this.isMinimapDragging = false;
	}

	private scrollFromMinimap(e: MouseEvent): void {
		const canvas = this.minimapCanvasRef?.nativeElement;
		const el = this.msGridRef?.nativeElement;
		const main = this.getScrollContainer();
		if (!canvas || !el) return;

		const canvasRect = canvas.getBoundingClientRect();
		const x = e.clientX - canvasRect.left;
		const y = e.clientY - canvasRect.top;
		const scale = canvas.width / el.scrollWidth;

		// Horizontal: grid scrolls itself
		el.scrollLeft = (x / scale) - (el.clientWidth / 2);

		// Vertical: <main> is the scroll container
		if (main) {
			// Grid's absolute position within main's scrollable content
			const gridAbsTop = el.getBoundingClientRect().top
				- main.getBoundingClientRect().top + main.scrollTop;
			main.scrollTop = gridAbsTop + (y / scale) - (main.clientHeight / 2);
		}
	}

	ngOnDestroy(): void {
		this.closeMinimap();
		document.removeEventListener('keydown', this.onKeyDown);
	}
}
