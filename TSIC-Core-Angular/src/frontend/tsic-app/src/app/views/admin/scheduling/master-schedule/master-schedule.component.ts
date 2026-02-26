import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import type { MasterScheduleResponse, MasterScheduleDay } from '@core/api';
import { MasterScheduleService } from './services/master-schedule.service';

@Component({
	selector: 'app-master-schedule',
	standalone: true,
	templateUrl: './master-schedule.component.html',
	styleUrl: './master-schedule.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterScheduleComponent implements OnInit {
	private readonly svc = inject(MasterScheduleService);

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
}
