import { Component, inject, signal, computed, ChangeDetectionStrategy, HostListener } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { UsLaxRankingsService } from '@infrastructure/services/uslax-rankings.service';
import { JobService } from '@infrastructure/services/job.service';
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

/** Row type for the unified master table */
interface MasterRow {
	type: 'matched' | 'manual' | 'unmatched';
	team: RankingsTeamDto;
	match: AlignedTeamDto | null;
}

// Single-tab page now — TabId kept for future extensibility
type TableFilter = 'all' | 'unmatched' | 'high' | 'medium';
type SaveThreshold = 'high' | 'medium' | 'all';
type SortCol = 'team' | 'club' | 'rank' | 'rankedAs' | 'conf';
type SortDir = 'asc' | 'desc';

/** Sentinel score for manually matched teams */
const MANUAL_MATCH_SCORE = -1;

@Component({
	selector: 'app-uslax-rankings',
	standalone: true,
	imports: [DecimalPipe, FormsModule, RouterLink, ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-rankings.component.html',
	styleUrl: './uslax-rankings.component.scss'
})
export class UsLaxRankingsComponent {
	private readonly rankingsService = inject(UsLaxRankingsService);
	private readonly jobService = inject(JobService);

	readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? 'your event');

	// ── (single-page, no tabs) ──

	// ── Dropdown options ──
	readonly scrapedAgeGroups = signal<AgeGroupOptionDto[]>([]);
	readonly registeredAgeGroups = signal<AgeGroupOptionDto[]>([]);

	// ── Selections ──
	readonly selectedScrapedAg = signal('');
	readonly selectedRegisteredAg = signal('');

	// ── Loading / messages ──
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly successMessage = signal<string | null>(null);

	// ── Alignment results ──
	readonly alignment = signal<AlignmentResultDto | null>(null);

	// ── Mutable match state ──
	readonly matchedTeams = signal<AlignedTeamDto[]>([]);
	readonly unmatchedRankings = signal<RankingEntryDto[]>([]);
	readonly unmatchedTeams = signal<RankingsTeamDto[]>([]);
	readonly totalTeamsInAgeGroup = signal(0);

	// ── Match interaction: single signal for which team row is in "pick a ranking" mode ──
	readonly activeMatchTeamId = signal<string | null>(null);

	// ── Toolbar filters ──
	readonly tableFilter = signal<TableFilter>('all');
	readonly sidebarSearch = signal('');
	readonly sidebarSortCol = signal<'rank' | 'team' | 'state' | 'rating'>('team');
	readonly sidebarSortDir = signal<SortDir>('asc');
	readonly saveThreshold = signal<SaveThreshold>('high');

	// ── Sort ──
	readonly sortCol = signal<SortCol | null>('conf');
	readonly sortDir = signal<SortDir>('desc');


	// ── Dialogs ──
	readonly showHelp = signal(false);
	readonly showClearConfirm = signal(false);
	readonly showSaveDropdown = signal(false);

	// ── Computed: master table rows (matched + unmatched in one list) ──
	readonly masterTableRows = computed<MasterRow[]>(() => {
		const matched = this.matchedTeams();
		const unmatched = this.unmatchedTeams();

		const rows: MasterRow[] = [
			...matched.map(m => ({
				type: (m.matchScore === MANUAL_MATCH_SCORE ? 'manual' : 'matched') as MasterRow['type'],
				team: m.registeredTeam,
				match: m
			})),
			...unmatched.map(t => ({
				type: 'unmatched' as const,
				team: t,
				match: null
			}))
		];

		// Sort: high confidence first, then medium, then manual, then unmatched
		return rows.sort((a, b) => {
			const scoreA = a.match?.matchScore ?? -2;
			const scoreB = b.match?.matchScore ?? -2;
			// Unmatched (-2) last, manual (-1) before unmatched, then by score descending
			if (scoreA === scoreB) return 0;
			if (scoreA === -2) return 1;
			if (scoreB === -2) return -1;
			if (scoreA === MANUAL_MATCH_SCORE && scoreB >= 0) return 1;
			if (scoreB === MANUAL_MATCH_SCORE && scoreA >= 0) return -1;
			return scoreB - scoreA;
		});
	});

	// ── Computed: filtered + sorted master table ──
	readonly filteredTableRows = computed(() => {
		const rows = this.masterTableRows();
		const filter = this.tableFilter();
		let filtered: MasterRow[];
		switch (filter) {
			case 'unmatched': filtered = rows.filter(r => r.type === 'unmatched'); break;
			case 'high': filtered = rows.filter(r => r.match && r.match.matchScore >= 0.75); break;
			case 'medium': filtered = rows.filter(r => r.match && r.match.matchScore >= 0.50 && r.match.matchScore < 0.75); break;
			default: filtered = rows;
		}
		const col = this.sortCol();
		const dir = this.sortDir();
		if (!col) return filtered;
		const mult = dir === 'asc' ? 1 : -1;
		return [...filtered].sort((a, b) => {
			let aVal: string | number = '';
			let bVal: string | number = '';
			switch (col) {
				case 'team': aVal = a.team.teamName; bVal = b.team.teamName; break;
				case 'club': aVal = a.team.clubName ?? ''; bVal = b.team.clubName ?? ''; break;
				case 'rank': aVal = a.match?.ranking.rank ?? 9999; bVal = b.match?.ranking.rank ?? 9999; break;
				case 'rankedAs': aVal = a.match?.ranking.team ?? ''; bVal = b.match?.ranking.team ?? ''; break;
				case 'conf': aVal = a.match?.matchScore ?? -2; bVal = b.match?.matchScore ?? -2; break;
			}
			if (typeof aVal === 'string') return aVal.localeCompare(bVal as string) * mult;
			return ((aVal as number) - (bVal as number)) * mult;
		});
	});

	// ── Computed: filtered + sorted sidebar rankings (unmatched only) ──
	readonly filteredSidebarRankings = computed(() => {
		let rankings = this.unmatchedRankings();
		const search = this.sidebarSearch().trim().toLowerCase();
		if (search) {
			rankings = rankings.filter(r =>
				r.team.toLowerCase().includes(search) ||
				r.state.toLowerCase().includes(search) ||
				String(r.rank).includes(search));
		}
		const col = this.sidebarSortCol();
		const mult = this.sidebarSortDir() === 'asc' ? 1 : -1;
		return [...rankings].sort((a, b) => {
			switch (col) {
				case 'rank': return (a.rank - b.rank) * mult;
				case 'team': return a.team.localeCompare(b.team) * mult;
				case 'state': return a.state.localeCompare(b.state) * mult;
				case 'rating': return (a.rating - b.rating) * mult;
				default: return 0;
			}
		});
	});

	// ── Computed: counts for summary chips ──
	readonly selectedAgName = computed(() => {
		const id = this.selectedRegisteredAg();
		return this.registeredAgeGroups().find(ag => ag.value === id)?.text ?? '';
	});
	readonly selectedScrapedAgName = computed(() => {
		const id = this.selectedScrapedAg();
		return this.scrapedAgeGroups().find(ag => ag.value === id)?.text ?? '';
	});
	readonly matchedCount = computed(() => this.matchedTeams().length);
	readonly unmatchedTeamCount = computed(() => this.unmatchedTeams().length);
	readonly totalRanked = computed(() => this.matchedTeams().length + this.unmatchedRankings().length);
	readonly matchRate = computed(() => {
		const total = this.totalTeamsInAgeGroup();
		return total > 0 ? this.matchedTeams().length / total : 0;
	});

	// ── Computed: save counts by threshold ──
	readonly highConfMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore >= 0.75));
	readonly mediumConfMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore >= 0.50 && a.matchScore < 0.75));
	readonly manualMatches = computed(() =>
		this.matchedTeams().filter(a => a.matchScore === MANUAL_MATCH_SCORE));

	readonly saveCountByThreshold = computed(() => {
		const high = this.highConfMatches().length;
		const medium = this.mediumConfMatches().length;
		const manual = this.manualMatches().length;
		return {
			high: high + manual,
			medium: high + medium + manual,
			all: this.matchedTeams().length
		};
	});

	readonly hasResults = computed(() => this.alignment() !== null);

	readonly hasDirtyMatches = computed(() =>
		this.matchedTeams().some(m => {
			const stored = this.parseRankingData(m.registeredTeam.nationalRankingData);
			return !stored || stored.rank !== m.ranking.rank || stored.team !== m.ranking.team;
		}));

	constructor() {
		this.loadAgeGroups();
	}

	@HostListener('document:keydown.escape')
	onEscape(): void {
		this.activeMatchTeamId.set(null);
		this.showSaveDropdown.set(false);
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

	// ── Dropdown change handlers ──

	onScrapedAgChange(value: string): void {
		this.selectedScrapedAg.set(value);
		this.autoAlign();
	}

	onRegisteredAgChange(value: string): void {
		this.selectedRegisteredAg.set(value);
		this.autoAlign();
	}

	private autoAlign(): void {
		if (this.selectedScrapedAg() && this.selectedRegisteredAg() && !this.hasResults()) {
			this.align();
		}
	}

	// ── Align ──

	align(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();
		if (!scraped || !registered) {
			this.errorMessage.set('Select both a national ranking source and a registered age group.');
			return;
		}

		const parts = scraped.split('|');
		const v = parts[0];
		const alpha = parts.length > 2 ? parts[1] : '';
		const yr = parts.length > 2 ? parts[2] : parts[1];

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);
		this.alignment.set(null);
		this.matchedTeams.set([]);
		this.unmatchedRankings.set([]);
		this.unmatchedTeams.set([]);
		this.activeMatchTeamId.set(null);
		this.tableFilter.set('all');

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

	// ── Manual match (unified: always from team → sidebar ranking) ──

	startMatch(teamId: string): void {
		this.activeMatchTeamId.set(this.activeMatchTeamId() === teamId ? null : teamId);
	}

	/** Called when user clicks a ranking in the sidebar while a team row is active.
	 *  Handles new match and reassign (sidebar only shows unmatched rankings). */
	assignRankingToActiveTeam(ranking: RankingEntryDto): void {
		const teamId = this.activeMatchTeamId();
		if (!teamId) return;

		// Check if this is a reassignment (team already matched) or new match
		const existingMatch = this.matchedTeams().find(m => m.registeredTeam.teamId === teamId);
		const unmatchedTeam = this.unmatchedTeams().find(t => t.teamId === teamId);

		if (existingMatch) {
			// Reassign: return old ranking to sidebar, assign new one
			this.unmatchedRankings.set(
				[...this.unmatchedRankings().filter(r => r.rank !== ranking.rank), existingMatch.ranking]
					.sort((a, b) => a.rank - b.rank));
			this.matchedTeams.set(this.matchedTeams().map(m =>
				m.registeredTeam.teamId === teamId
					? { ranking, registeredTeam: m.registeredTeam, matchScore: MANUAL_MATCH_SCORE, matchReason: 'Manual reassignment by user' }
					: m));
			this.persistRanking(teamId, ranking, MANUAL_MATCH_SCORE);
		} else if (unmatchedTeam) {
			// New manual match
			this.matchedTeams.set([...this.matchedTeams(), {
				ranking, registeredTeam: unmatchedTeam, matchScore: MANUAL_MATCH_SCORE, matchReason: 'Manual match by user'
			}]);
			this.unmatchedRankings.set(this.unmatchedRankings().filter(r => r.rank !== ranking.rank));
			this.unmatchedTeams.set(this.unmatchedTeams().filter(t => t.teamId !== teamId));
			this.persistRanking(teamId, ranking, MANUAL_MATCH_SCORE);
		}

		this.activeMatchTeamId.set(null);
	}

	undoMatch(match: AlignedTeamDto): void {
		this.matchedTeams.set(this.matchedTeams().filter(m => m !== match));
		this.unmatchedRankings.set(
			[...this.unmatchedRankings(), match.ranking].sort((a, b) => a.rank - b.rank));
		this.unmatchedTeams.set([...this.unmatchedTeams(), match.registeredTeam]);
	}

	isManualMatch(match: AlignedTeamDto): boolean {
		return match.matchScore === MANUAL_MATCH_SCORE;
	}

	// ── Persist ──

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

	// ── Save (merged from old tab 2) ──

	toggleSaveDropdown(): void {
		this.showSaveDropdown.set(!this.showSaveDropdown());
	}

	saveWithThreshold(threshold: SaveThreshold): void {
		this.saveThreshold.set(threshold);
		this.showSaveDropdown.set(false);

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

		// Map threshold to confidence category for the backend
		const confidenceCategory = threshold === 'high' ? 'high' : 'medium';

		this.isSaving.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);

		if (threshold === 'all') {
			// Save all matched — use saveAllMatchedRankings approach
			this.saveAllMatchedSequential();
			return;
		}

		this.rankingsService.importRankings({
			registeredTeamAgeGroupId: registered,
			confidenceCategory,
			v, alpha, yr,
			clubWeight: 75,
			teamWeight: 25
		}).subscribe({
			next: result => {
				const manualOnes = this.manualMatches();
				if (manualOnes.length > 0) {
					this.saveManualMatchesBatch(manualOnes, result);
				} else {
					this.isSaving.set(false);
					if (result.success) {
						this.successMessage.set(result.message ?? `Saved ${result.updatedCount} team rankings.`);
					} else {
						this.errorMessage.set(result.message ?? 'Save failed.');
					}
				}
			},
			error: (err: { error?: { message?: string } }) => {
				this.isSaving.set(false);
				this.errorMessage.set(err.error?.message ?? 'Save failed.');
			}
		});
	}

	private saveManualMatchesBatch(matches: AlignedTeamDto[], importResult: ImportRankingsResultDto): void {
		let saved = 0;
		let failed = 0;
		const total = matches.length;

		const saveNext = (index: number): void => {
			if (index >= total) {
				this.isSaving.set(false);
				const autoCount = importResult.updatedCount;
				this.successMessage.set(
					`Saved ${autoCount} auto-matched + ${saved} manual team rankings.` +
					(failed > 0 ? ` (${failed} manual saves failed)` : ''));
				return;
			}
			const match = matches[index];
			const json = this.buildRankingJson(match.ranking, match.matchScore);
			this.rankingsService.updateTeamRanking(match.registeredTeam.teamId, json).subscribe({
				next: () => { saved++; this.updateLocalRankingData(match.registeredTeam.teamId, json); saveNext(index + 1); },
				error: () => { failed++; saveNext(index + 1); }
			});
		};
		saveNext(0);
	}

	private saveAllMatchedSequential(): void {
		const matches = this.matchedTeams();
		if (matches.length === 0) { this.isSaving.set(false); return; }
		let saved = 0;
		let failed = 0;

		const saveNext = (index: number): void => {
			if (index >= matches.length) {
				this.isSaving.set(false);
				this.successMessage.set(
					`Saved ${saved} team rankings.` + (failed > 0 ? ` (${failed} failed)` : ''));
				return;
			}
			const match = matches[index];
			const json = this.buildRankingJson(match.ranking, match.matchScore);
			this.rankingsService.updateTeamRanking(match.registeredTeam.teamId, json).subscribe({
				next: () => { saved++; this.updateLocalRankingData(match.registeredTeam.teamId, json); saveNext(index + 1); },
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
		const ag = this.selectedRegisteredAg();
		if (!ag) return;

		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.rankingsService.clearTeamRankings(ag).subscribe({
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

	// ── Filter + Sort ──

	toggleSidebarSort(col: 'rank' | 'team' | 'state' | 'rating'): void {
		if (this.sidebarSortCol() === col) {
			this.sidebarSortDir.set(this.sidebarSortDir() === 'asc' ? 'desc' : 'asc');
		} else {
			this.sidebarSortCol.set(col);
			this.sidebarSortDir.set('asc');
		}
	}

	sidebarSortIcon(col: string): string {
		if (this.sidebarSortCol() !== col) return 'bi-chevron-expand';
		return this.sidebarSortDir() === 'asc' ? 'bi-chevron-up' : 'bi-chevron-down';
	}

	setFilter(filter: TableFilter): void {
		this.tableFilter.set(filter);
	}

	toggleSort(col: SortCol): void {
		if (this.sortCol() === col) {
			this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
		} else {
			this.sortCol.set(col);
			this.sortDir.set('asc');
		}
	}

	sortIcon(col: SortCol): string {
		if (this.sortCol() !== col) return 'bi-chevron-expand';
		return this.sortDir() === 'asc' ? 'bi-chevron-up' : 'bi-chevron-down';
	}

	// ── Helpers ──

	confidenceClass(score: number): string {
		if (score === MANUAL_MATCH_SCORE) return '';
		if (score >= 0.75) return 'confidence-high';
		if (score >= 0.50) return 'confidence-medium';
		return 'confidence-low';
	}

	formatPercent(score: number): string {
		if (score === MANUAL_MATCH_SCORE) return '—';
		if (isNaN(score)) return '—';
		return `${Math.round(score * 100)}%`;
	}

	private buildRankingJson(ranking: RankingEntryDto, matchScore: number): string {
		const dto: NationalRankingDataDto = {
			rank: ranking.rank, team: ranking.team, state: ranking.state,
			record: ranking.record, rating: ranking.rating, agd: ranking.agd,
			sched: ranking.sched, matchScore, matchedAt: new Date().toISOString()
		};
		return JSON.stringify(dto);
	}

	private parseRankingData(json: string | null | undefined): NationalRankingDataDto | null {
		if (!json) return null;
		try { return JSON.parse(json) as NationalRankingDataDto; }
		catch { return null; }
	}

	/** Returns drift info if saved rank differs from current match rank */
	getSavedDrift(row: MasterRow): { savedRank: number; savedDate: string } | null {
		if (!row.match) return null;
		const saved = this.parseRankingData(row.team.nationalRankingData);
		if (!saved) return null;
		if (saved.rank === row.match.ranking.rank) return null;
		const date = saved.matchedAt ? new Date(saved.matchedAt) : null;
		const dateStr = date ? `${date.getMonth() + 1}/${date.getDate()}/${date.getFullYear()}` : '';
		return { savedRank: saved.rank, savedDate: dateStr };
	}

	/** True if this row has saved data (regardless of drift) */
	isSaved(row: MasterRow): boolean {
		return !!row.team.nationalRankingData;
	}

}
