import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RefereeAssignmentService } from '@infrastructure/services/referee-assignment.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type {
	RefereeSummaryDto,
	RefScheduleFilterOptionsDto,
	RefScheduleGameDto,
	RefGameDetailsDto,
	ImportRefereesResult,
	CopyGameRefsRequest,
} from '@core/api';

// ── Grid cell: one game in the field x time matrix ──
interface GridCell {
	game: RefScheduleGameDto;
	assignedRefs: RefereeSummaryDto[];
}

// ── Grid row: one timeslot across all fields ──
interface GridRow {
	time: string;
	timeLabel: string;
	cells: Map<string, GridCell[]>;
}

@Component({
	selector: 'app-referee-assignment',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent],
	templateUrl: './referee-assignment.component.html',
	styleUrl: './referee-assignment.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefereeAssignmentComponent implements OnInit {
	private readonly svc = inject(RefereeAssignmentService);

	// ── Core data ──
	readonly referees = signal<RefereeSummaryDto[]>([]);
	readonly filterOptions = signal<RefScheduleFilterOptionsDto | null>(null);
	readonly games = signal<RefScheduleGameDto[]>([]);
	readonly isLoading = signal(false);
	readonly errorMessage = signal('');

	// ── Filter state ──
	readonly selectedDays = signal<string[]>([]);
	readonly selectedTimes = signal<string[]>([]);
	readonly selectedAgegroups = signal<string[]>([]);
	readonly selectedFields = signal<string[]>([]);

	// ── Computed: filter option lists ──
	readonly gameDays = computed(() => this.filterOptions()?.gameDays ?? []);
	readonly gameTimes = computed(() => this.filterOptions()?.gameTimes ?? []);
	readonly agegroups = computed(() => this.filterOptions()?.agegroups ?? []);
	readonly fields = computed(() => this.filterOptions()?.fields ?? []);

	readonly hasActiveFilters = computed(() =>
		this.selectedDays().length > 0 ||
		this.selectedTimes().length > 0 ||
		this.selectedAgegroups().length > 0 ||
		this.selectedFields().length > 0
	);

	// ── Active referees only ──
	readonly activeReferees = computed(() =>
		this.referees().filter(r => r.isActive)
	);

	// ── Grid computation: group flat games into field x time matrix ──
	readonly gridColumns = computed(() => {
		const gameList = this.games();
		const fieldMap = new Map<string, string>();
		for (const g of gameList) {
			const fid = g.fieldId ?? '__none__';
			if (!fieldMap.has(fid)) {
				fieldMap.set(fid, g.fieldName ?? 'Unassigned');
			}
		}
		return Array.from(fieldMap.entries()).map(([id, name]) => ({ id, name }));
	});

	readonly gridRows = computed(() => {
		const gameList = this.games();
		const refs = this.referees();
		const refMap = new Map<string, RefereeSummaryDto>();
		for (const r of refs) {
			refMap.set(r.registrationId, r);
		}

		// Group by time
		const timeMap = new Map<string, RefScheduleGameDto[]>();
		for (const g of gameList) {
			const time = this.extractTime(g.gameDate);
			if (!timeMap.has(time)) {
				timeMap.set(time, []);
			}
			timeMap.get(time)!.push(g);
		}

		// Sort times chronologically
		const sortedTimes = Array.from(timeMap.keys()).sort();

		const rows: GridRow[] = [];
		for (const time of sortedTimes) {
			const gamesAtTime = timeMap.get(time)!;
			const cellMap = new Map<string, GridCell[]>();

			for (const g of gamesAtTime) {
				const fid = g.fieldId ?? '__none__';
				if (!cellMap.has(fid)) {
					cellMap.set(fid, []);
				}
				const assignedRefs = g.assignedRefIds
					.map(id => refMap.get(id))
					.filter((r): r is RefereeSummaryDto => r != null);
				cellMap.get(fid)!.push({ game: g, assignedRefs });
			}

			rows.push({
				time,
				timeLabel: this.formatTimeLabel(time),
				cells: cellMap,
			});
		}
		return rows;
	});

	// ── Ref dropdown state ──
	readonly openDropdownGid = signal<number | null>(null);

	// ── Copy modal state ──
	readonly showCopyModal = signal(false);
	readonly copyGid = signal(0);
	readonly copyDirection = signal<'down' | 'up'>('down');
	readonly copyTimeslots = signal(1);
	readonly copySkip = signal(0);
	readonly isCopyLoading = signal(false);
	readonly copyResult = signal<number[] | null>(null);

	// ── Info modal state ──
	readonly showInfoModal = signal(false);
	readonly infoGid = signal(0);
	readonly infoDetails = signal<RefGameDetailsDto[]>([]);
	readonly isInfoLoading = signal(false);

	// ── Import modal state ──
	readonly showImportModal = signal(false);
	readonly importFile = signal<File | null>(null);
	readonly isImportLoading = signal(false);
	readonly importResult = signal<ImportRefereesResult | null>(null);

	// ── Seed modal state ──
	readonly showSeedModal = signal(false);
	readonly seedCount = signal(10);
	readonly isSeedLoading = signal(false);
	readonly seedResult = signal<number | null>(null);

	// ── Delete confirmation modal state ──
	readonly showDeleteModal = signal(false);
	readonly deleteConfirmText = signal('');
	readonly isDeleteLoading = signal(false);

	@ViewChild('gridScrollContainer') gridScrollEl?: ElementRef<HTMLElement>;

	// ══════════════════════════════════════════════════════════════
	// Lifecycle
	// ══════════════════════════════════════════════════════════════

	ngOnInit(): void {
		this.loadInitialData();
	}

	private loadInitialData(): void {
		this.isLoading.set(true);
		this.svc.getFilterOptions().subscribe({
			next: (opts) => {
				this.filterOptions.set(opts);
				this.isLoading.set(false);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message ?? 'Failed to load filter options');
				this.isLoading.set(false);
			},
		});
		this.svc.getReferees().subscribe({
			next: (refs) => this.referees.set(refs),
			error: () => { /* referees loaded silently */ },
		});
	}

	// ══════════════════════════════════════════════════════════════
	// Search / Filter
	// ══════════════════════════════════════════════════════════════

	searchGames(): void {
		this.isLoading.set(true);
		this.errorMessage.set('');
		this.openDropdownGid.set(null);

		this.svc.searchSchedule({
			gameDays: this.selectedDays().length ? this.selectedDays() : null,
			gameTimes: this.selectedTimes().length ? this.selectedTimes() : null,
			agegroupIds: this.selectedAgegroups().length ? this.selectedAgegroups() : null,
			fieldIds: this.selectedFields().length ? this.selectedFields() : null,
		}).subscribe({
			next: (data) => {
				this.games.set(data);
				this.isLoading.set(false);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message ?? 'Search failed');
				this.isLoading.set(false);
			},
		});
	}

	resetFilters(): void {
		this.selectedDays.set([]);
		this.selectedTimes.set([]);
		this.selectedAgegroups.set([]);
		this.selectedFields.set([]);
		this.games.set([]);
		this.openDropdownGid.set(null);
	}

	// ── Multi-select helpers ──

	onMultiSelectChange(event: Event, target: 'days' | 'times' | 'agegroups' | 'fields'): void {
		const selectEl = event.target as HTMLSelectElement;
		const values = Array.from(selectEl.selectedOptions).map(o => o.value);
		switch (target) {
			case 'days': this.selectedDays.set(values); break;
			case 'times': this.selectedTimes.set(values); break;
			case 'agegroups': this.selectedAgegroups.set(values); break;
			case 'fields': this.selectedFields.set(values); break;
		}
	}

	// ══════════════════════════════════════════════════════════════
	// Ref Assignment (per game)
	// ══════════════════════════════════════════════════════════════

	toggleRefDropdown(gid: number): void {
		this.openDropdownGid.set(this.openDropdownGid() === gid ? null : gid);
	}

	isRefAssigned(game: RefScheduleGameDto, refId: string): boolean {
		return game.assignedRefIds.includes(refId);
	}

	toggleRefAssignment(game: RefScheduleGameDto, refId: string): void {
		const currentIds = [...game.assignedRefIds];
		const idx = currentIds.indexOf(refId);
		if (idx >= 0) {
			currentIds.splice(idx, 1);
		} else {
			currentIds.push(refId);
		}

		// Optimistically update the local state
		const updatedGames = this.games().map(g =>
			g.gid === game.gid ? { ...g, assignedRefIds: currentIds } : g
		);
		this.games.set(updatedGames);

		// POST to backend
		this.svc.assignRefs({ gid: game.gid, refRegistrationIds: currentIds }).subscribe({
			error: () => {
				// Revert on error
				const reverted = this.games().map(g =>
					g.gid === game.gid ? { ...g, assignedRefIds: game.assignedRefIds } : g
				);
				this.games.set(reverted);
			},
		});
	}

	getAssignedRefNames(game: RefScheduleGameDto): string {
		const refMap = new Map<string, RefereeSummaryDto>();
		for (const r of this.referees()) {
			refMap.set(r.registrationId, r);
		}
		return game.assignedRefIds
			.map(id => {
				const ref = refMap.get(id);
				return ref ? `${ref.firstName} ${ref.lastName}` : 'Unknown';
			})
			.join(', ');
	}

	// ══════════════════════════════════════════════════════════════
	// Copy Modal
	// ══════════════════════════════════════════════════════════════

	openCopyModal(gid: number, direction: 'down' | 'up'): void {
		this.copyGid.set(gid);
		this.copyDirection.set(direction);
		this.copyTimeslots.set(1);
		this.copySkip.set(0);
		this.copyResult.set(null);
		this.isCopyLoading.set(false);
		this.showCopyModal.set(true);
	}

	closeCopyModal(): void {
		this.showCopyModal.set(false);
	}

	executeCopy(): void {
		this.isCopyLoading.set(true);
		const request: CopyGameRefsRequest = {
			gid: this.copyGid(),
			copyDown: this.copyDirection() === 'down',
			numberTimeslots: this.copyTimeslots(),
			skipInterval: this.copySkip(),
		};
		this.svc.copyGameRefs(request).subscribe({
			next: (affectedGids) => {
				this.copyResult.set(affectedGids);
				this.isCopyLoading.set(false);
				// Refresh games to reflect copied assignments
				this.searchGames();
			},
			error: () => {
				this.isCopyLoading.set(false);
			},
		});
	}

	// ══════════════════════════════════════════════════════════════
	// Info Modal
	// ══════════════════════════════════════════════════════════════

	openInfoModal(gid: number): void {
		this.infoGid.set(gid);
		this.infoDetails.set([]);
		this.isInfoLoading.set(true);
		this.showInfoModal.set(true);

		this.svc.getGameDetails(gid).subscribe({
			next: (details) => {
				this.infoDetails.set(details);
				this.isInfoLoading.set(false);
			},
			error: () => {
				this.isInfoLoading.set(false);
			},
		});
	}

	closeInfoModal(): void {
		this.showInfoModal.set(false);
	}

	// ══════════════════════════════════════════════════════════════
	// Import Modal
	// ══════════════════════════════════════════════════════════════

	downloadImportTemplate(): void {
		const headers = 'FirstName,LastName,Email,Cellphone,CertificationNumber';
		const blob = new Blob([headers + '\n'], { type: 'text/csv' });
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = 'referee-import-template.csv';
		a.click();
		URL.revokeObjectURL(url);
	}

	openImportModal(): void {
		this.importFile.set(null);
		this.importResult.set(null);
		this.isImportLoading.set(false);
		this.showImportModal.set(true);
	}

	closeImportModal(): void {
		this.showImportModal.set(false);
	}

	onFileSelected(event: Event): void {
		const input = event.target as HTMLInputElement;
		if (input.files?.length) {
			this.importFile.set(input.files[0]);
		}
	}

	executeImport(): void {
		const file = this.importFile();
		if (!file) return;

		this.isImportLoading.set(true);
		this.svc.importReferees(file).subscribe({
			next: (result) => {
				this.importResult.set(result);
				this.isImportLoading.set(false);
				// Refresh referees list
				this.svc.getReferees().subscribe({
					next: (refs) => this.referees.set(refs),
				});
			},
			error: () => {
				this.isImportLoading.set(false);
			},
		});
	}

	// ══════════════════════════════════════════════════════════════
	// Seed Modal
	// ══════════════════════════════════════════════════════════════

	openSeedModal(): void {
		this.seedCount.set(10);
		this.seedResult.set(null);
		this.isSeedLoading.set(false);
		this.showSeedModal.set(true);
	}

	closeSeedModal(): void {
		this.showSeedModal.set(false);
	}

	executeSeed(): void {
		this.isSeedLoading.set(true);
		this.svc.seedTestReferees(this.seedCount()).subscribe({
			next: (refs) => {
				this.seedResult.set(refs.length);
				this.isSeedLoading.set(false);
				// Refresh referees list
				this.svc.getReferees().subscribe({
					next: (allRefs) => this.referees.set(allRefs),
				});
			},
			error: () => {
				this.isSeedLoading.set(false);
			},
		});
	}

	// ══════════════════════════════════════════════════════════════
	// Delete All Modal
	// ══════════════════════════════════════════════════════════════

	openDeleteModal(): void {
		this.deleteConfirmText.set('');
		this.isDeleteLoading.set(false);
		this.showDeleteModal.set(true);
	}

	closeDeleteModal(): void {
		this.showDeleteModal.set(false);
	}

	executeDelete(): void {
		if (this.deleteConfirmText() !== 'DELETE') return;

		this.isDeleteLoading.set(true);
		this.svc.purgeAll().subscribe({
			next: () => {
				this.referees.set([]);
				this.games.set([]);
				this.isDeleteLoading.set(false);
				this.showDeleteModal.set(false);
			},
			error: () => {
				this.isDeleteLoading.set(false);
			},
		});
	}

	// ══════════════════════════════════════════════════════════════
	// Helpers
	// ══════════════════════════════════════════════════════════════

	/** Extract time portion (HH:mm) from ISO date string for grouping */
	private extractTime(isoDate: string): string {
		const d = new Date(isoDate);
		return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
	}

	/** Format time label for grid row headers */
	private formatTimeLabel(time: string): string {
		const [hStr, mStr] = time.split(':');
		const h = parseInt(hStr, 10);
		const suffix = h >= 12 ? 'PM' : 'AM';
		const display = h === 0 ? 12 : h > 12 ? h - 12 : h;
		return `${display}:${mStr} ${suffix}`;
	}

	/** Get the cell data for a specific row and column */
	getCellGames(row: GridRow, colId: string): GridCell[] {
		return row.cells.get(colId) ?? [];
	}

	/** Track by for @for loops */
	trackByGid(_index: number, cell: GridCell): number {
		return cell.game.gid;
	}

	/** Close dropdown when clicking outside */
	onDocumentClick(event: MouseEvent): void {
		const target = event.target as HTMLElement;
		if (!target.closest('.ref-dropdown-container')) {
			this.openDropdownGid.set(null);
		}
	}
}
