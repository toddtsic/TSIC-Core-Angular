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
	ImportRankingsResultDto,
} from '@core/api';

/** Client-side shape for the NationalRankingData JSON blob stored on teams */
interface NationalRankingDataDto {
	rank: number;
	team: string;
	state: string;
	record: string;
	rating: number;
	agd: number;
	sched: number;
	matchScore: number;
	matchedAt: string;
}

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
	readonly importResult = signal<ImportRankingsResultDto | null>(null);

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

	/** True when any matched team's expected ranking JSON differs from its stored NationalRankingData */
	readonly hasDirtyMatches = computed(() =>
		this.matchedTeams().some(m => {
			const stored = this.parseRankingData(m.registeredTeam.nationalRankingData);
			return !stored || stored.rank !== m.ranking.rank || stored.team !== m.ranking.team;
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
			error: (err: { error?: { message?: string } }) => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Failed to align rankings.');
			}
		});
	}

	// ── Manual match ──

	startMatchFromTeam(teamId: string): void {
		this.matchingTeamId.set(this.matchingTeamId() === teamId ? null : teamId);
		this.matchingRankIndex.set(null);
	}

	startMatchFromRanking(rankIndex: number): void {
		this.matchingRankIndex.set(this.matchingRankIndex() === rankIndex ? null : rankIndex);
		this.matchingTeamId.set(null);
	}

	selectTeamForRanking(ranking: RankingEntryDto, team: RankingsTeamDto): void {
		this.applyManualMatch(ranking, team);
		this.matchingRankIndex.set(null);
	}

	selectRankingForTeam(team: RankingsTeamDto, ranking: RankingEntryDto): void {
		this.applyManualMatch(ranking, team);
		this.matchingTeamId.set(null);
	}

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

		this.persistRanking(team.teamId, ranking, MANUAL_MATCH_SCORE);
	}

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

	reassignMatch(currentMatch: AlignedTeamDto, newRanking: RankingEntryDto): void {
		if (!newRanking) {
			this.reassigningTeamId.set(null);
			return;
		}

		this.unmatchedRankings.set(
			[...this.unmatchedRankings().filter(r => r.rank !== newRanking.rank), currentMatch.ranking]
				.sort((a, b) => a.rank - b.rank));

		this.matchedTeams.set(this.matchedTeams().map(m =>
			m === currentMatch
				? { ranking: newRanking, registeredTeam: currentMatch.registeredTeam,
					matchScore: MANUAL_MATCH_SCORE, matchReason: 'Manual reassignment by user' }
				: m));

		this.reassigningTeamId.set(null);

		this.persistRanking(currentMatch.registeredTeam.teamId, newRanking, MANUAL_MATCH_SCORE);
	}

	/** Serialize ranking data to JSON and save to NationalRankingData */
	private persistRanking(teamId: string, ranking: RankingEntryDto, matchScore: number): void {
		const json = this.buildRankingJson(ranking, matchScore);
		this.rankingsService.updateTeamRanking(teamId, json).subscribe({
			next: () => this.updateLocalRankingData(teamId, json),
			error: (err: { error?: { message?: string } }) =>
				this.errorMessage.set(err.error?.message ?? 'Failed to save ranking.')
		});
	}

	private updateLocalRankingData(teamId: string, json: string): void {
		this.matchedTeams.set(this.matchedTeams().map(m =>
			m.registeredTeam.teamId === teamId
				? { ...m, registeredTeam: { ...m.registeredTeam, nationalRankingData: json } }
				: m));
	}

	// ── Import ──

	importRankings(): void {
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

		this.rankingsService.importRankings({
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
			error: (err: { error?: { message?: string } }) => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Import failed.');
			}
		});
	}

	private saveManualMatchesBatch(matches: AlignedTeamDto[], importResult: ImportRankingsResultDto): void {
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
			const json = this.buildRankingJson(match.ranking, match.matchScore);
			this.rankingsService.updateTeamRanking(match.registeredTeam.teamId, json).subscribe({
				next: () => {
					saved++;
					this.updateLocalRankingData(match.registeredTeam.teamId, json);
					saveNext(index + 1);
				},
				error: () => { failed++; saveNext(index + 1); }
			});
		};

		saveNext(0);
	}

	// ── Save all matched team rankings ──

	saveAllMatchedRankings(): void {
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
					`Updated ${saved} team rankings.` +
					(failed > 0 ? ` (${failed} failed)` : ''));
				return;
			}

			const match = matches[index];
			const json = this.buildRankingJson(match.ranking, match.matchScore);
			this.rankingsService.updateTeamRanking(match.registeredTeam.teamId, json).subscribe({
				next: () => {
					saved++;
					this.updateLocalRankingData(match.registeredTeam.teamId, json);
					saveNext(index + 1);
				},
				error: () => { failed++; saveNext(index + 1); }
			});
		};

		saveNext(0);
	}

	// ── Clear rankings ──

	confirmClearRankings(): void {
		if (!this.selectedRegisteredAg()) {
			this.errorMessage.set('Select a registered age group first.');
			return;
		}
		this.showClearConfirm.set(true);
	}

	clearRankings(): void {
		this.showClearConfirm.set(false);
		const registered = this.selectedRegisteredAg();
		if (!registered) {
			this.errorMessage.set('Select a registered age group first.');
			return;
		}

		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.rankingsService.clearTeamRankings(registered).subscribe({
			next: () => {
				this.isLoading.set(false);
				this.successMessage.set('Team rankings cleared.');
				this.matchedTeams.set(this.matchedTeams().map(m =>
					({ ...m, registeredTeam: { ...m.registeredTeam, nationalRankingData: null } })));
			},
			error: (err: { error?: { message?: string } }) => {
				this.isLoading.set(false);
				this.errorMessage.set(err.error?.message ?? 'Failed to clear rankings.');
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
		const match = this.matchedTeams().find(m => m.registeredTeam.teamId === teamId);
		if (!match) return;
		const json = this.buildRankingJson(match.ranking, match.matchScore);
		this.rankingsService.updateTeamRanking(teamId, json).subscribe({
			next: () => {
				this.editingTeamId.set(null);
				this.updateLocalRankingData(teamId, json);
				this.successMessage.set('Ranking updated.');
			},
			error: (err: { error?: { message?: string } }) =>
				this.errorMessage.set(err.error?.message ?? 'Update failed.')
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
			error: (err: { error?: { message?: string } }) =>
				this.errorMessage.set(err.error?.message ?? 'Export failed.')
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

	/** Build a NationalRankingDataDto JSON string from a ranking entry */
	private buildRankingJson(ranking: RankingEntryDto, matchScore: number): string {
		const dto: NationalRankingDataDto = {
			rank: ranking.rank,
			team: ranking.team,
			state: ranking.state,
			record: ranking.record,
			rating: ranking.rating,
			agd: ranking.agd,
			sched: ranking.sched,
			matchScore,
			matchedAt: new Date().toISOString()
		};
		return JSON.stringify(dto);
	}

	/** Safely parse NationalRankingData JSON, returns null on failure */
	private parseRankingData(json: string | null | undefined): NationalRankingDataDto | null {
		if (!json) return null;
		try {
			return JSON.parse(json) as NationalRankingDataDto;
		} catch {
			return null;
		}
	}
}
