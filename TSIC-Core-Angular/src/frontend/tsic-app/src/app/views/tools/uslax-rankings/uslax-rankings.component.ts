import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UsLaxRankingsService } from '@infrastructure/services/uslax-rankings.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type {
	AgeGroupOptionDto,
	AlignmentResultDto,
	AlignedTeamDto,
	RankingEntryDto,
	RankingsTeamDto,
	ImportCommentsResultDto
} from '@core/api';

type TabId = 'align' | 'import' | 'manage';

/** Sentinel score for manually matched teams */
const MANUAL_MATCH_SCORE = -1;

@Component({
	selector: 'app-uslax-rankings',
	standalone: true,
	imports: [DecimalPipe, FormsModule, ConfirmDialogComponent],
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

	// ── Scrape parameters ──
	readonly selectedScrapedAg = signal('');
	readonly selectedRegisteredAg = signal('');

	// ── Loading / error state ──
	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly successMessage = signal<string | null>(null);

	// ── Alignment results (immutable API response for metadata) ──
	readonly alignment = signal<AlignmentResultDto | null>(null);
	readonly importResult = signal<ImportCommentsResultDto | null>(null);

	// ── Mutable match state (populated from API, modified by manual overrides) ──
	readonly matchedTeams = signal<AlignedTeamDto[]>([]);
	readonly unmatchedRankings = signal<RankingEntryDto[]>([]);
	readonly unmatchedTeams = signal<RankingsTeamDto[]>([]);
	readonly totalTeamsInAgeGroup = signal(0);

	// ── Confidence filter for import ──
	readonly confidenceCategory = signal<'high' | 'medium'>('high');

	// ── Help / confirm dialogs ──
	readonly showHelp = signal(false);
	readonly showClearConfirm = signal(false);

	// ── Inline edit state ──
	readonly editingTeamId = signal<string | null>(null);
	readonly editComment = signal('');

	// ── Manual match selection state ──
	readonly matchingTeamId = signal<string | null>(null);
	readonly matchingRankIndex = signal<number | null>(null);
	readonly reassigningTeamId = signal<string | null>(null);
	readonly isSavingComments = signal(false);

	// ── Computed views ──
	readonly highConfMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore >= 0.75));

	readonly mediumConfMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore >= 0.50 && a.matchScore < 0.75));

	readonly manualMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore === MANUAL_MATCH_SCORE));

	readonly filteredMatches = computed(() => {
		const high = this.highConfMatches();
		const medium = this.mediumConfMatches();
		const manual = this.manualMatches();
		return this.confidenceCategory() === 'high'
			? [...high, ...manual]
			: [...high, ...medium, ...manual];
	});

	readonly hasResults = computed(() => this.alignment() !== null);

	/** True when any matched team's expected comment differs from its stored teamComments */
	readonly hasDirtyMatches = computed(() =>
		this.matchedTeams().some(m => {
			const expected = `${m.ranking.rank.toString().padStart(3, '0')}:${m.ranking.team}`;
			return m.registeredTeam.teamComments !== expected;
		}));

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

		const parts = scraped.split('|');
		const v = parts[0];
		const alpha = parts.length > 2 ? parts[1] : '';
		const yr = parts.length > 2 ? parts[2] : parts[1];

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.alignment.set(null);
		this.matchedTeams.set([]);
		this.unmatchedRankings.set([]);
		this.unmatchedTeams.set([]);
		this.matchingTeamId.set(null);
		this.matchingRankIndex.set(null);

		this.rankingsService.alignRankings(v, alpha, yr, registered).subscribe({
			next: result => {
				this.alignment.set(result);
				this.matchedTeams.set([...result.alignedTeams]);
				this.unmatchedRankings.set([...result.unmatchedRankings]);
				this.unmatchedTeams.set([...result.unmatchedTeams]);
				this.totalTeamsInAgeGroup.set(result.totalTeamsInAgeGroup);
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

	// ── Manual match ──

	/** Start matching from unmatched registered team → pick a ranking */
	startMatchFromTeam(teamId: string): void {
		this.matchingTeamId.set(this.matchingTeamId() === teamId ? null : teamId);
		this.matchingRankIndex.set(null);
	}

	/** Start matching from unmatched ranking → pick a registered team */
	startMatchFromRanking(rankIndex: number): void {
		this.matchingRankIndex.set(this.matchingRankIndex() === rankIndex ? null : rankIndex);
		this.matchingTeamId.set(null);
	}

	/** Complete manual match: registered team selected from ranking picker */
	selectTeamForRanking(ranking: RankingEntryDto, team: RankingsTeamDto): void {
		this.applyManualMatch(ranking, team);
		this.matchingRankIndex.set(null);
	}

	/** Complete manual match: ranking selected from team picker */
	selectRankingForTeam(team: RankingsTeamDto, ranking: RankingEntryDto): void {
		this.applyManualMatch(ranking, team);
		this.matchingTeamId.set(null);
	}

	/** Move a ranking+team pair from unmatched → matched with Manual confidence */
	private applyManualMatch(ranking: RankingEntryDto, team: RankingsTeamDto): void {
		const newMatch: AlignedTeamDto = {
			ranking,
			registeredTeam: team,
			matchScore: MANUAL_MATCH_SCORE,
			matchReason: 'Manual match by user'
		};

		this.matchedTeams.set([...this.matchedTeams(), newMatch]);
		this.unmatchedRankings.set(this.unmatchedRankings().filter(r => r.rank !== ranking.rank));
		this.unmatchedTeams.set(this.unmatchedTeams().filter(t => t.teamId !== team.teamId));

		// Immediately persist the comment
		this.persistComment(team.teamId, ranking);
	}

	/** Remove a manual match → move pieces back to unmatched */
	undoManualMatch(match: AlignedTeamDto): void {
		this.matchedTeams.set(this.matchedTeams().filter(m => m !== match));
		this.unmatchedRankings.set([...this.unmatchedRankings(), match.ranking]
			.sort((a, b) => a.rank - b.rank));
		this.unmatchedTeams.set([...this.unmatchedTeams(), match.registeredTeam]);
	}

	isManualMatch(match: AlignedTeamDto): boolean {
		return match.matchScore === MANUAL_MATCH_SCORE;
	}

	// ── Reassign matched team to a different ranking ──

	startReassign(teamId: string): void {
		this.reassigningTeamId.set(this.reassigningTeamId() === teamId ? null : teamId);
		this.matchingTeamId.set(null);
		this.matchingRankIndex.set(null);
	}

	/** Swap current ranking for a new one: old ranking → unmatched, new ranking → matched */
	reassignMatch(currentMatch: AlignedTeamDto, newRanking: RankingEntryDto): void {
		if (!newRanking) {
			this.reassigningTeamId.set(null);
			return;
		}

		// Send old ranking back to unmatched
		this.unmatchedRankings.set(
			[...this.unmatchedRankings().filter(r => r.rank !== newRanking.rank), currentMatch.ranking]
				.sort((a, b) => a.rank - b.rank));

		// Replace the match with the new ranking (marked Manual)
		this.matchedTeams.set(this.matchedTeams().map(m =>
			m === currentMatch
				? { ranking: newRanking, registeredTeam: currentMatch.registeredTeam,
					matchScore: MANUAL_MATCH_SCORE, matchReason: 'Manual reassignment by user' }
				: m));

		this.reassigningTeamId.set(null);

		// Immediately persist the updated comment
		this.persistComment(currentMatch.registeredTeam.teamId, newRanking);
	}

	/** Save a single team comment and update local teamComments to keep dirty tracking in sync */
	private persistComment(teamId: string, ranking: RankingEntryDto): void {
		const comment = `${ranking.rank.toString().padStart(3, '0')}:${ranking.team}`;
		this.rankingsService.updateTeamComment(teamId, comment).subscribe({
			next: () => this.updateLocalTeamComments(teamId, comment),
			error: err => this.errorMessage.set(err.error?.message ?? 'Failed to save comment.')
		});
	}

	/** Patch teamComments on the local signal entry so hasDirtyMatches recomputes */
	private updateLocalTeamComments(teamId: string, comment: string): void {
		this.matchedTeams.set(this.matchedTeams().map(m =>
			m.registeredTeam.teamId === teamId
				? { ...m, registeredTeam: { ...m.registeredTeam, teamComments: comment } }
				: m));
	}

	// ── Import ──

	importComments(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();

		if (!scraped || !registered) {
			this.errorMessage.set('Run alignment first.');
			return;
		}

		const importParts = scraped.split('|');
		const v = importParts[0];
		const alpha = importParts.length > 2 ? importParts[1] : '';
		const yr = importParts.length > 2 ? importParts[2] : importParts[1];

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);

		// Save auto-matches via backend bulk import
		this.rankingsService.importComments({
			registeredTeamAgeGroupId: registered,
			confidenceCategory: this.confidenceCategory(),
			v,
			alpha,
			yr,
			clubWeight: 75,
			teamWeight: 25
		}).subscribe({
			next: result => {
				this.importResult.set(result);

				// Also save manual matches individually
				const manualOnes = this.manualMatches();
				if (manualOnes.length > 0) {
					this.saveManualMatchesBatch(manualOnes, result);
				} else {
					this.isLoading.set(false);
					if (result.success) {
						this.successMessage.set(result.message ?? `Updated ${result.updatedCount} teams.`);
					} else {
						this.errorMessage.set(result.message ?? 'Import failed.');
					}
				}
			},
			error: err => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Import failed.');
			}
		});
	}

	/** Save manual matches one at a time (DbContext is not thread-safe) */
	private saveManualMatchesBatch(matches: AlignedTeamDto[], importResult: ImportCommentsResultDto): void {
		let saved = 0;
		let failed = 0;
		const total = matches.length;

		const saveNext = (index: number): void => {
			if (index >= total) {
				this.isLoading.set(false);
				const autoCount = importResult.updatedCount;
				this.successMessage.set(
					`Updated ${autoCount} auto-matched + ${saved} manual teams.` +
					(failed > 0 ? ` (${failed} manual saves failed)` : ''));
				return;
			}

			const match = matches[index];
			const comment = `${match.ranking.rank.toString().padStart(3, '0')}:${match.ranking.team}`;
			this.rankingsService.updateTeamComment(match.registeredTeam.teamId, comment).subscribe({
				next: () => {
					saved++;
					this.updateLocalTeamComments(match.registeredTeam.teamId, comment);
					saveNext(index + 1);
				},
				error: () => { failed++; saveNext(index + 1); }
			});
		};

		saveNext(0);
	}

	// ── Save all matched team comments ──

	saveAllMatchedComments(): void {
		const matches = this.matchedTeams();
		if (matches.length === 0) return;

		this.isSavingComments.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);
		let saved = 0;
		let failed = 0;

		const saveNext = (index: number): void => {
			if (index >= matches.length) {
				this.isSavingComments.set(false);
				this.successMessage.set(
					`Updated ${saved} team comments.` +
					(failed > 0 ? ` (${failed} failed)` : ''));
				return;
			}

			const match = matches[index];
			const comment = `${match.ranking.rank.toString().padStart(3, '0')}:${match.ranking.team}`;
			this.rankingsService.updateTeamComment(match.registeredTeam.teamId, comment).subscribe({
				next: () => {
					saved++;
					this.updateLocalTeamComments(match.registeredTeam.teamId, comment);
					saveNext(index + 1);
				},
				error: () => { failed++; saveNext(index + 1); }
			});
		};

		saveNext(0);
	}

	// ── Clear comments ──

	confirmClearComments(): void {
		if (!this.selectedRegisteredAg()) {
			this.errorMessage.set('Select a registered age group first.');
			return;
		}
		this.showClearConfirm.set(true);
	}

	clearComments(): void {
		this.showClearConfirm.set(false);
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
				// Clear local teamComments so UI reflects the change
				this.matchedTeams.set(this.matchedTeams().map(m =>
					({ ...m, registeredTeam: { ...m.registeredTeam, teamComments: null } })));
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
		const comment = this.editComment();
		this.rankingsService.updateTeamComment(teamId, comment).subscribe({
			next: () => {
				this.editingTeamId.set(null);
				this.updateLocalTeamComments(teamId, comment);
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

		const csvParts = scraped.split('|');
		const v = csvParts[0];
		const alpha = csvParts.length > 2 ? csvParts[1] : '';
		const yr = csvParts.length > 2 ? csvParts[2] : csvParts[1];
		this.rankingsService.exportCsv(v, alpha, yr, registered).subscribe({
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
		if (score === MANUAL_MATCH_SCORE) return 'confidence-manual';
		if (score >= 0.75) return 'confidence-high';
		if (score >= 0.50) return 'confidence-medium';
		return 'confidence-low';
	}

	formatPercent(score: number): string {
		if (score === MANUAL_MATCH_SCORE) return 'Manual';
		return `${Math.round(score * 100)}%`;
	}
}
