import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UsLaxRankingsService } from '@infrastructure/services/uslax-rankings.service';
import type {
	AgeGroupOptionDto,
	AlignmentResultDto,
	AlignedTeamDto,
	ImportCommentsResultDto
} from '@core/api';

type TabId = 'align' | 'import' | 'manage';

@Component({
	selector: 'app-uslax-rankings',
	standalone: true,
	imports: [DecimalPipe, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-rankings.component.html',
	styleUrl: './uslax-rankings.component.scss'
})
export class UsLaxRankingsComponent {
	private readonly rankingsService = inject(UsLaxRankingsService);

	// ── Tab state ──
	readonly activeTab = signal<TabId>('align');

	// ── Dropdown options ──
	readonly scrapedAgeGroups = signal<AgeGroupOptionDto[]>([]);
	readonly registeredAgeGroups = signal<AgeGroupOptionDto[]>([]);

	// ── Scrape parameters (Girls National hardcoded per user request) ──
	readonly selectedScrapedAg = signal('');
	readonly selectedRegisteredAg = signal('');

	// ── Loading / error state ──
	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly successMessage = signal<string | null>(null);

	// ── Alignment results ──
	readonly alignment = signal<AlignmentResultDto | null>(null);
	readonly importResult = signal<ImportCommentsResultDto | null>(null);

	// ── Confidence filter for import ──
	readonly confidenceCategory = signal<'high' | 'medium'>('high');

	// ── Inline edit state ──
	readonly editingTeamId = signal<string | null>(null);
	readonly editComment = signal('');

	// ── Computed views ──
	readonly highConfMatches = computed(() =>
		this.alignment()?.alignedTeams?.filter(a => a.matchScore >= 0.75) ?? []);

	readonly mediumConfMatches = computed(() =>
		this.alignment()?.alignedTeams?.filter(a => a.matchScore >= 0.50 && a.matchScore < 0.75) ?? []);

	readonly filteredMatches = computed(() =>
		this.confidenceCategory() === 'high' ? this.highConfMatches() : [
			...this.highConfMatches(),
			...this.mediumConfMatches()
		]);

	readonly hasResults = computed(() => this.alignment() !== null);

	constructor() {
		this.loadAgeGroups();
	}

	// ── Tab navigation ──

	selectTab(tab: TabId): void {
		this.activeTab.set(tab);
		this.errorMessage.set(null);
		this.successMessage.set(null);
	}

	// ── Load age groups ──

	private loadAgeGroups(): void {
		this.rankingsService.getScrapedAgeGroups().subscribe({
			next: groups => this.scrapedAgeGroups.set(groups),
			error: () => this.scrapedAgeGroups.set([])
		});

		this.rankingsService.getRegisteredAgeGroups().subscribe({
			next: groups => this.registeredAgeGroups.set(groups),
			error: () => this.registeredAgeGroups.set([])
		});
	}

	// ── Align ──

	align(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();

		if (!scraped || !registered) {
			this.errorMessage.set('Select both a scraped age group and a registered age group.');
			return;
		}

		// Parse scraped value: format is "v|yr" (e.g., "21|2029")
		const [v, yr] = scraped.split('|');

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.alignment.set(null);

		this.rankingsService.alignRankings(v, '', yr, registered).subscribe({
			next: result => {
				this.alignment.set(result);
				this.isLoading.set(false);
				if (!result.success) {
					this.errorMessage.set(result.errorMessage ?? 'Alignment failed.');
				}
			},
			error: err => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Failed to align rankings.');
			}
		});
	}

	// ── Import ──

	importComments(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();

		if (!scraped || !registered) {
			this.errorMessage.set('Run alignment first.');
			return;
		}

		const [v, yr] = scraped.split('|');

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);

		this.rankingsService.importComments({
			registeredTeamAgeGroupId: registered,
			confidenceCategory: this.confidenceCategory(),
			v,
			alpha: '',
			yr,
			clubWeight: 75,
			teamWeight: 25
		}).subscribe({
			next: result => {
				this.importResult.set(result);
				this.isLoading.set(false);
				if (result.success) {
					this.successMessage.set(result.message ?? `Updated ${result.updatedCount} teams.`);
				} else {
					this.errorMessage.set(result.message ?? 'Import failed.');
				}
			},
			error: err => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Import failed.');
			}
		});
	}

	// ── Clear comments ──

	clearComments(): void {
		const registered = this.selectedRegisteredAg();
		if (!registered) {
			this.errorMessage.set('Select a registered age group first.');
			return;
		}

		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.rankingsService.clearTeamComments(registered).subscribe({
			next: () => {
				this.isLoading.set(false);
				this.successMessage.set('Team comments cleared.');
			},
			error: err => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Failed to clear comments.');
			}
		});
	}

	// ── Inline edit ──

	startEdit(match: AlignedTeamDto): void {
		this.editingTeamId.set(match.registeredTeam.teamId);
		this.editComment.set(`${match.ranking.rank.toString().padStart(3, '0')}:${match.ranking.team}`);
	}

	cancelEdit(): void {
		this.editingTeamId.set(null);
		this.editComment.set('');
	}

	saveEdit(teamId: string): void {
		this.rankingsService.updateTeamComment(teamId, this.editComment()).subscribe({
			next: () => {
				this.editingTeamId.set(null);
				this.successMessage.set('Comment updated.');
			},
			error: err => this.errorMessage.set(err.error?.message ?? 'Update failed.')
		});
	}

	// ── Export CSV ──

	exportCsv(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();
		if (!scraped || !registered) return;

		const [v, yr] = scraped.split('|');
		this.rankingsService.exportCsv(v, '', yr, registered).subscribe({
			next: blob => {
				const url = URL.createObjectURL(blob);
				const a = document.createElement('a');
				a.href = url;
				a.download = `uslax-rankings-${yr}.csv`;
				a.click();
				URL.revokeObjectURL(url);
			},
			error: err => this.errorMessage.set(err.error?.message ?? 'Export failed.')
		});
	}

	// ── Helpers ──

	confidenceClass(score: number): string {
		if (score >= 0.75) return 'confidence-high';
		if (score >= 0.50) return 'confidence-medium';
		return 'confidence-low';
	}

	formatPercent(score: number): string {
		return `${Math.round(score * 100)}%`;
	}
}
