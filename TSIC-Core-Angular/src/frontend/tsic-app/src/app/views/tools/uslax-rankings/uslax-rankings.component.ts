import { Component, inject, signal, computed, ChangeDetectionStrategy, DestroyRef, HostListener } from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, take, switchMap } from 'rxjs/operators';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { UsLaxRankingsService } from '@infrastructure/services/uslax-rankings.service';
import { JobService } from '@infrastructure/services/job.service';
import { TeamSearchService } from '@views/search/teams/services/team-search.service';
import { isTournament } from '@infrastructure/constants/job-type.constants';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import {
	TeamRenameConfirmComponent,
	type TeamRenameConfirmation,
} from '@shared/teams/team-rename-confirm.component';
import type {
	AgeGroupOptionDto,
	AlignmentResultDto,
	AlignedTeamDto,
	NationalRankingDataDto,
	RankingEntryDto,
	RankingsTeamDto,
	RankingSeasonDto,
	SaveRankingEntry,
	SaveRankingsResultDto,
} from '@core/api';

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
	imports: [DecimalPipe, FormsModule, RouterLink, ConfirmDialogComponent, TeamRenameConfirmComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-rankings.component.html',
	styleUrl: './uslax-rankings.component.scss'
})
export class UsLaxRankingsComponent {
	private readonly rankingsService = inject(UsLaxRankingsService);
	private readonly jobService = inject(JobService);
	/** Reused for the local-team pencil — the admin door, gated AdminOnly. See renameTeam(). */
	private readonly teamSearchService = inject(TeamSearchService);
	private readonly destroyRef = inject(DestroyRef);

	readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? 'your event');

	/**
	 * Matching registered teams to national rankings exists to seed tournament pools, so
	 * it is tournament-only (the API enforces the same rule). Browsing the published
	 * rankings stays open to every event type — hence two capabilities, one screen.
	 */
	readonly canMatch = computed(() => isTournament(this.jobService.currentJob()?.jobTypeId));

	// ── (single-page, no tabs) ──

	// ── Dropdown options ──
	readonly seasons = signal<RankingSeasonDto[]>([]);
	readonly scrapedAgeGroups = signal<AgeGroupOptionDto[]>([]);
	readonly registeredAgeGroups = signal<AgeGroupOptionDto[]>([]);

	// ── Selections ──
	readonly selectedSeason = signal('');
	readonly selectedScrapedAg = signal('');
	readonly selectedRegisteredAg = signal('');

	// ── Loading / messages ──
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly successMessage = signal<string | null>(null);

	/**
	 * EVERY team in the selected age group, each carrying whatever stamp it already holds — read
	 * from our own database the moment an age group is picked, before anything is scraped.
	 *
	 * This is the screen's starting point now: your own roster, renameable, with the already-ranked
	 * teams marked. It also answers "did my save take?", which the old design could not do without
	 * re-scraping and re-matching — which shows a fresh guess rather than what is stored. A
	 * third-party site being down must never stop a director reading their own data.
	 */
	readonly agegroupTeams = signal<RankingsTeamDto[]>([]);
	readonly isLoadingSaved = signal(false);

	/** Denominator for "N of M", known without a scrape. */
	readonly savedTotalTeams = computed(() => this.agegroupTeams().length);

	readonly savedCount = computed(() => this.agegroupTeams().filter(t => t.nationalRankingData).length);

	/** The roster is on screen — enough to render the table, with or without a match run. */
	readonly hasTeams = computed(() => this.agegroupTeams().length > 0);

	/** Accordion under the summary line. Collapsed by default — the count usually suffices. */
	readonly showSavedList = signal(false);

	/**
	 * The stamped teams, ranked first. This reads the DATABASE, not the current scrape, so it
	 * stays right when no match has been run and when the saved set disagrees with a fresh one.
	 */
	readonly savedRows = computed(() =>
		this.agegroupTeams()
			.map(t => ({ team: t, data: this.parseRankingData(t.nationalRankingData) }))
			.filter((r): r is { team: RankingsTeamDto; data: NationalRankingDataDto } => r.data !== null)
			.sort((a, b) => a.data.rank - b.data.rank)
			.map(({ team, data }) => ({
				teamId: team.teamId,
				teamName: team.clubName ? `${team.clubName}:${team.teamName}` : team.teamName,
				rank: data.rank,
				// The ranked team as usclublax.com lists it. The rank rides in the badge beside it,
				// so it is NOT repeated here — one column, one statement: this is the national team
				// we matched to, and this is where it sits. No record: it is frozen at save time,
				// and a stale W-L presented as current is worse than no W-L at all.
				matchedTo: data.team,
				tooltip: `${data.team}\nRating: ${data.rating} | Record: ${data.record}`
					+ `\nAGD: ${data.agd} | Sched: ${data.sched}`
			})));

	/** The row whose pairing is being deleted — its trash button disables while the post is out. */
	readonly removingTeamId = signal<string | null>(null);

	/** Most recent save across the age group — the "as of" on the summary line. */
	readonly savedAsOf = computed(() => {
		let latest: Date | null = null;
		for (const t of this.agegroupTeams()) {
			const d = this.parseRankingData(t.nationalRankingData);
			if (!d?.matchedAt) continue;
			const when = new Date(d.matchedAt);
			if (isNaN(when.getTime())) continue;
			if (!latest || when > latest) latest = when;
		}
		return latest ? `${latest.getMonth() + 1}/${latest.getDate()}/${latest.getFullYear()}` : '';
	});

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
	// Team name, ascending. The director arrives knowing a team's NAME, not its confidence
	// score — sorting by match quality put the list in an order nothing on screen explains
	// and made "is my team here?" a scan of every row.
	readonly sortCol = signal<SortCol | null>('team');
	readonly sortDir = signal<SortDir>('asc');


	// ── Dialogs ──
	readonly showClearConfirm = signal(false);
	readonly showSaveDropdown = signal(false);

	/** Team whose name the pencil is editing, or null when the rename dialog is closed. */
	readonly renamingTeam = signal<RankingsTeamDto | null>(null);
	readonly renameBusy = signal(false);
	readonly renameError = signal<string | null>(null);

	/** Team currently being re-scored after a rename, so its row can show it. */
	readonly isReassessing = signal<string | null>(null);
	/** Teams whose current pairing came from a post-rename re-check — flagged in the table. */
	readonly reassessedTeamIds = signal<ReadonlySet<string>>(new Set());

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
				// Sort on what the cell SHOWS — "Club:Team" — not on teamName alone. Sorting by
				// the bare team name ordered the list by "Black", "2031 Crush", "Fire", which
				// matches nothing the eye can follow down the column.
				case 'team': aVal = this.displayTeamName(a.team); bVal = this.displayTeamName(b.team); break;
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
	readonly filteredSidebarRankings = computed(() =>
		this.filterAndSortRankings(this.unmatchedRankings()));

	private filterAndSortRankings(source: RankingEntryDto[]): RankingEntryDto[] {
		let rankings = source;
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
	}

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
		this.loadSeasons();
		this.loadRegisteredAgeGroups();
	}

	@HostListener('document:keydown.escape')
	onEscape(): void {
		this.activeMatchTeamId.set(null);
		this.showSaveDropdown.set(false);
		if (this.renamingTeam() && !this.renameBusy()) this.closeRename();
	}


	// ── Load seasons + age groups ──

	/**
	 * Two ways this can come back with nothing, and they are not the same fact:
	 * a 502 means we never reached usclublax.com and retrying may work; an empty 200
	 * means we did reach it and it published nothing. Say which — an unexplained empty
	 * dropdown is the failure this screen was reported for.
	 */
	private loadSeasons(): void {
		this.rankingsService.getSeasons().subscribe({
			next: seasons => {
				this.seasons.set(seasons);
				const current = seasons.find(s => s.isCurrent) ?? seasons[0];
				this.selectedSeason.set(current?.value ?? '');
				// Omit yr on the first load so we take whatever season the site serves,
				// rather than asserting one it may not publish.
				this.loadScrapedAgeGroups();
			},
			error: (err: { error?: { message?: string } }) => {
				this.seasons.set([]);
				this.errorMessage.set(err.error?.message
					?? "Couldn't reach usclublax.com to read the list of seasons.");
				// Still try the age groups: without a season the site serves its current
				// one, so the screen may be usable even with the season list missing.
				this.loadScrapedAgeGroups();
			}
		});
	}

	/**
	 * The set of age groups differs season to season — older seasons publish 12 where the
	 * current one publishes 28 — so this re-runs on every season change rather than
	 * filtering one cached list.
	 */
	private loadScrapedAgeGroups(yr?: string): void {
		this.rankingsService.getScrapedAgeGroups(yr).subscribe({
			next: groups => {
				this.scrapedAgeGroups.set(groups);
				// Either loader can finish last, so both attempt the guess; it is a no-op once a
				// national group is selected.
				this.preselectNationalAgeGroup();
				if (groups.length === 0) {
					const season = this.seasons().find(s => s.value === this.selectedSeason())?.text;
					this.errorMessage.set(season
						? `usclublax.com published no girls age groups for ${season}.`
						: 'usclublax.com published no girls age groups.');
				}
			},
			error: (err: { error?: { message?: string } }) => {
				this.scrapedAgeGroups.set([]);
				this.errorMessage.set(err.error?.message
					?? "Couldn't reach usclublax.com to read the age groups.");
			}
		});
	}

	/**
	 * Job metadata arrives from an async GET and this route has no resolver, so at construction
	 * `currentJob()` is routinely still null — `isTournament(undefined)` is false. The previous
	 * version read the signal once, returned on that false, and never looked again: on a cold load
	 * (a hard refresh straight onto this URL) the matching UI would then render with a permanently
	 * empty age-group dropdown. It self-healed on an in-app navigation, which is exactly why it was
	 * reported as age groups "occasionally" dropping.
	 *
	 * `toObservable` + `filter` + `take(1)` waits for the job to actually arrive instead of
	 * sampling once. `take(1)` because this only ever needs to fire on the first tournament job
	 * this component instance sees — a cross-job switch destroys and rebuilds the view.
	 */
	private loadRegisteredAgeGroups(): void {
		toObservable(this.jobService.currentJob)
			.pipe(
				filter(job => isTournament(job?.jobTypeId)),
				take(1),
				switchMap(() => this.rankingsService.getRegisteredAgeGroups()),
				takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: groups => {
					this.registeredAgeGroups.set(groups);
					if (groups.length === 0) {
						this.errorMessage.set(
							'No age groups with active teams were found for this event.');
					}
				},
				// Say so. Silently emptying the list is indistinguishable from an event that
				// genuinely has no age groups, and it is the same reported symptom.
				error: (err: unknown) => {
					this.registeredAgeGroups.set([]);
					this.errorMessage.set(extractHttpErrorMessage(
						err, "Couldn't load this event's age groups. Refresh to try again."));
				}
			});
	}

	// ── Dropdown change handlers ──

	onSeasonChange(value: string): void {
		this.selectedSeason.set(value);
		// A group from the old season may not exist in the new one — clear rather than
		// carry a selection that would scrape a URL the site does not serve.
		this.selectedScrapedAg.set('');
		this.scrapedAgeGroups.set([]);
		this.resetToRoster();
		this.loadScrapedAgeGroups(value);
	}

	onScrapedAgChange(value: string): void {
		this.selectedScrapedAg.set(value);
		this.resetToRoster();
		if (value && this.selectedRegisteredAg()) this.align();
	}

	/**
	 * The age group is the SUBJECT of this screen, and picking it is the first thing you do.
	 *
	 * It loads your teams straight from our database — no scrape — so the table is your roster
	 * before it is a match sheet: renameable, with the already-ranked teams marked. It also
	 * enables the two usclublax dropdowns, which stay disabled until this point because there is
	 * nothing to match against without it.
	 *
	 * Changing it must wipe the previous group's results. It did not before — the old guard was
	 * `!this.hasResults()`, so once an alignment existed this handler did nothing, the table kept
	 * showing group A while Save and Clear silently retargeted group B.
	 */
	onRegisteredAgChange(value: string): void {
		if (value === this.selectedRegisteredAg()) return;
		this.selectedRegisteredAg.set(value);
		// The national group was chosen for the OLD age group; carrying it over would match this
		// roster against another class's rankings.
		this.selectedScrapedAg.set('');
		this.agegroupTeams.set([]);
		this.resetToRoster();
		if (value) this.loadAgeGroupTeams(value);
	}

	/**
	 * Read this age group's teams from our own database. Deliberately independent of the scrape:
	 * a director must be able to see and correct their own roster when usclublax.com is down.
	 */
	private loadAgeGroupTeams(agegroupId: string): void {
		this.isLoadingSaved.set(true);
		this.rankingsService.getAgeGroupTeams(agegroupId)
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: teams => {
					// Guard a slow response for a group the user has since moved off.
					if (this.selectedRegisteredAg() !== agegroupId) return;
					this.isLoadingSaved.set(false);
					this.agegroupTeams.set(teams);
					this.totalTeamsInAgeGroup.set(teams.length);
					// Seeding the unmatched list is what puts the roster in the table: every team
					// starts unpaired, and matching moves them across.
					this.unmatchedTeams.set([...teams]);
					this.preselectNationalAgeGroup();
				},
				error: (err: unknown) => {
					if (this.selectedRegisteredAg() !== agegroupId) return;
					this.isLoadingSaved.set(false);
					this.agegroupTeams.set([]);
					this.errorMessage.set(extractHttpErrorMessage(
						err, "Couldn't read this age group's teams."));
				}
			});
	}

	/**
	 * Re-read the stored stamps after a save, so the summary line is the database's claim rather
	 * than ours. Updates ONLY `agegroupTeams` — re-seeding the table here would discard the match
	 * set the director is still working on.
	 */
	private refreshStoredStamps(agegroupId: string): void {
		this.rankingsService.getAgeGroupTeams(agegroupId)
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: teams => {
					if (this.selectedRegisteredAg() !== agegroupId) return;
					this.agegroupTeams.set(teams);
				},
				// Silent: the save already reported its own outcome, and a failed re-read says
				// nothing about whether it landed.
				error: () => { /* keep the pre-save view rather than blanking it */ }
			});
	}

	/**
	 * Guess the national group from the age group's own name and run the match.
	 *
	 * Both sides are named by graduation class, so "2031" on our side belongs with "Girls 2031" on
	 * theirs — a mechanical pairing the director should not have to make 6 times per event. Age
	 * groups with no class year in the name ("OPEN", "Varsity") get no guess and wait for a choice.
	 *
	 * Called from both loaders because either can finish last; it does nothing once a national
	 * group is selected, so it never overrides a deliberate pick.
	 */
	private preselectNationalAgeGroup(): void {
		if (this.selectedScrapedAg()) return;
		if (!this.selectedRegisteredAg() || this.scrapedAgeGroups().length === 0) return;
		if (this.agegroupTeams().length === 0) return;

		const year = /\b(20\d{2})\b/.exec(this.selectedAgName())?.[1];
		if (!year) return;

		const guess = this.scrapedAgeGroups().find(ag => ag.text.includes(year));
		if (!guess) return;

		this.selectedScrapedAg.set(guess.value);
		this.align();
	}

	/** Splits the dropdown value, which the API hands us as "v|alpha|yr". */
	private parseAgValue(value: string): { v: string; alpha: string; yr: string } {
		const parts = value.split('|');
		return {
			v: parts[0],
			alpha: parts.length > 2 ? parts[1] : '',
			yr: parts.length > 2 ? parts[2] : parts[1]
		};
	}

	/**
	 * Drop the match results but KEEP the roster — the table falls back to every team unpaired,
	 * rather than emptying. The roster belongs to the age group; the pairings belong to a scrape.
	 */
	private resetToRoster(): void {
		this.alignment.set(null);
		this.matchedTeams.set([]);
		this.unmatchedRankings.set([]);
		this.unmatchedTeams.set([...this.agegroupTeams()]);
		this.activeMatchTeamId.set(null);
		this.errorMessage.set(null);
		this.successMessage.set(null);
		// "matched after rename" describes a pairing in THIS result set. Carrying the flag into
		// the next one would label a row the re-check never touched.
		this.reassessedTeamIds.set(new Set());
		this.isReassessing.set(null);
	}

	// ── Align ──

	align(): void {
		const scraped = this.selectedScrapedAg();
		const registered = this.selectedRegisteredAg();
		if (!scraped || !registered) {
			this.errorMessage.set('Select both a national ranking source and a registered age group.');
			return;
		}

		const { v, alpha, yr } = this.parseAgValue(scraped);

		this.isLoading.set(true);
		// resetToRoster, not an empty table: the roster stays on screen while the scrape runs, so
		// the director never watches their teams disappear and come back.
		this.resetToRoster();
		this.tableFilter.set('all');

		this.rankingsService.alignRankings(v, alpha, yr, registered)
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: result => {
					this.isLoading.set(false);
					if (!result.success) {
						// Keep the roster visible — a failed scrape says nothing about our teams.
						this.errorMessage.set(result.errorMessage ?? 'Alignment failed.');
						return;
					}
					this.alignment.set(result);
					this.matchedTeams.set([...result.alignedTeams]);
					this.unmatchedRankings.set([...result.unmatchedRankings]);
					this.unmatchedTeams.set([...result.unmatchedTeams]);
					this.totalTeamsInAgeGroup.set(result.totalTeamsInAgeGroup);
				},
				error: (err: unknown) => {
					this.isLoading.set(false);
					this.errorMessage.set(extractHttpErrorMessage(err, 'Failed to align rankings.'));
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
			// Screen state only. A hand match is committed by Save, exactly like every other row —
			// the old code wrote it to the database on the click, so half the table was already
			// committed and half was not, and un-matching had no way to take that write back.
		} else if (unmatchedTeam) {
			// New manual match
			this.matchedTeams.set([...this.matchedTeams(), {
				ranking, registeredTeam: unmatchedTeam, matchScore: MANUAL_MATCH_SCORE, matchReason: 'Manual match by user'
			}]);
			this.unmatchedRankings.set(this.unmatchedRankings().filter(r => r.rank !== ranking.rank));
			this.unmatchedTeams.set(this.unmatchedTeams().filter(t => t.teamId !== teamId));
			// Screen state only. A hand match is committed by Save, exactly like every other row —
			// the old code wrote it to the database on the click, so half the table was already
			// committed and half was not, and un-matching had no way to take that write back.
		}

		this.activeMatchTeamId.set(null);
	}

	undoMatch(match: AlignedTeamDto): void {
		this.matchedTeams.set(this.matchedTeams().filter(m => m !== match));
		this.unmatchedRankings.set(
			[...this.unmatchedRankings(), match.ranking].sort((a, b) => a.rank - b.rank));
		this.unmatchedTeams.set([...this.unmatchedTeams(), match.registeredTeam]);
	}

	/**
	 * Delete ONE team's stored national ranking, from the accordion. A single-entry save with
	 * `ranking: null` — omission means "leave alone", so the null is what makes it a clear.
	 *
	 * It also undoes the match on screen if that team is currently paired: without that the row
	 * still reads as matched and the next Save writes the stamp straight back.
	 */
	removeStoredPairing(teamId: string): void {
		const registered = this.selectedRegisteredAg();
		if (!registered || this.removingTeamId()) return;

		this.removingTeamId.set(teamId);
		this.errorMessage.set(null);
		this.successMessage.set(null);

		this.rankingsService.saveRankings({
			registeredTeamAgeGroupId: registered,
			teams: [{ teamId, ranking: null }]
		})
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: result => {
					this.removingTeamId.set(null);
					if (!result.success) {
						this.errorMessage.set(result.message ?? 'Could not remove the ranking.');
						return;
					}
					const match = this.matchedTeams().find(m => m.registeredTeam.teamId === teamId);
					if (match) this.undoMatch(match);
					this.successMessage.set('National ranking removed.');
					this.refreshStoredStamps(registered);
				},
				error: (err: unknown) => {
					this.removingTeamId.set(null);
					this.errorMessage.set(extractHttpErrorMessage(err, 'Could not remove the ranking.'));
				}
			});
	}

	isManualMatch(match: AlignedTeamDto): boolean {
		return match.matchScore === MANUAL_MATCH_SCORE;
	}

	// ── Save ──

	toggleSaveDropdown(): void {
		this.showSaveDropdown.set(!this.showSaveDropdown());
	}

	/**
	 * Save what is on the screen. One request, carrying the reviewed decisions themselves.
	 *
	 * The threshold picks which matches are WRITTEN; it never decides what is erased. Matches below
	 * it are simply omitted from the payload and keep whatever they already have — a director
	 * saving at "75%+" must not silently wipe the medium-confidence stamps they chose to keep.
	 * The only rows that clear are the ones they explicitly un-matched.
	 */
	saveWithThreshold(threshold: SaveThreshold): void {
		this.saveThreshold.set(threshold);
		this.showSaveDropdown.set(false);

		const registered = this.selectedRegisteredAg();
		if (!registered) {
			this.errorMessage.set('Select an age group first.');
			return;
		}
		if (!this.hasResults()) {
			this.errorMessage.set('Run Look Up Rankings first.');
			return;
		}

		const minScore = threshold === 'high' ? 0.75 : threshold === 'medium' ? 0.50 : 0;
		const teams: SaveRankingEntry[] = [];

		// Writes. Manual matches carry the sentinel score and are always included: the director
		// chose them by hand, which outranks any threshold.
		for (const match of this.matchedTeams()) {
			if (match.matchScore !== MANUAL_MATCH_SCORE && match.matchScore < minScore) continue;
			teams.push({
				teamId: match.registeredTeam.teamId,
				ranking: this.buildRankingData(match.ranking, match.matchScore)
			});
		}

		// Clears. A team that is unmatched on screen but still carries a stored stamp is an
		// un-match the director performed — the one thing the old save could never persist,
		// because the server re-derived the match and wrote it straight back.
		for (const team of this.unmatchedTeams()) {
			if (this.storedRankingFor(team.teamId)) {
				teams.push({ teamId: team.teamId, ranking: null });
			}
		}

		if (teams.length === 0) {
			this.errorMessage.set('Nothing to save at this confidence level.');
			return;
		}

		this.isSaving.set(true);
		this.errorMessage.set(null);
		this.successMessage.set(null);

		this.rankingsService.saveRankings({ registeredTeamAgeGroupId: registered, teams })
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: result => {
					this.isSaving.set(false);
					// Report the server's verdict, both ways. The previous version checked
					// `success` on only one of its two branches, so a failed save with any manual
					// match on screen announced itself in green.
					if (!result.success) {
						this.errorMessage.set(result.message ?? 'Save failed.');
						return;
					}
					this.successMessage.set(result.message ?? this.describeSave(result));
					this.applySavedLocally(teams);
					// Re-read from the database rather than trusting the local patch — this is the
					// screen's claim that the save landed, so it should be the database's claim.
					this.refreshStoredStamps(registered);
				},
				error: (err: unknown) => {
					this.isSaving.set(false);
					this.errorMessage.set(extractHttpErrorMessage(err, 'Save failed.'));
				}
			});
	}

	private describeSave(result: SaveRankingsResultDto): string {
		return result.clearedCount > 0
			? `Saved ${result.updatedCount} team rankings, cleared ${result.clearedCount}.`
			: `Saved ${result.updatedCount} team rankings.`;
	}

	/** Mirror the committed payload onto the in-memory rows so the table reflects it immediately. */
	private applySavedLocally(entries: SaveRankingEntry[]): void {
		const byTeam = new Map(entries.map(e => [e.teamId, e.ranking]));
		this.matchedTeams.set(this.matchedTeams().map(m => {
			if (!byTeam.has(m.registeredTeam.teamId)) return m;
			const ranking = byTeam.get(m.registeredTeam.teamId);
			return {
				...m,
				registeredTeam: {
					...m.registeredTeam,
					nationalRankingData: ranking ? JSON.stringify(ranking) : null
				}
			};
		}));
		this.unmatchedTeams.set(this.unmatchedTeams().map(t =>
			byTeam.has(t.teamId) && !byTeam.get(t.teamId)
				? { ...t, nationalRankingData: null }
				: t));
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
				this.unmatchedTeams.set(this.unmatchedTeams().map(t =>
					({ ...t, nationalRankingData: null })));
				// Strip the stamps but KEEP the roster: clearing rankings deletes ranking data,
				// not teams. Emptying this signal would blank the table itself.
				this.agegroupTeams.set(this.agegroupTeams().map(t =>
					({ ...t, nationalRankingData: null })));
			},
			error: (err: unknown) => {
				this.isLoading.set(false);
				this.errorMessage.set(extractHttpErrorMessage(err, 'Failed to clear rankings.'));
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

	/** The team as the table and the accordion both print it: "Club:Team", or bare when clubless. */
	displayTeamName(team: RankingsTeamDto): string {
		return team.clubName ? `${team.clubName}:${team.teamName}` : team.teamName;
	}

	formatPercent(score: number): string {
		if (score === MANUAL_MATCH_SCORE) return '—';
		if (isNaN(score)) return '—';
		return `${Math.round(score * 100)}%`;
	}

	/**
	 * Build the stamp. `season` and `rankingSource` come from the age-group dropdown value the
	 * scrape was run against, so a stored rank records WHICH season it came from — without that,
	 * a rank carried over from last season is indistinguishable from this season's on the team
	 * row, and Pool Assignment sorts the two as if they were comparable.
	 */
	private buildRankingData(ranking: RankingEntryDto, matchScore: number): NationalRankingDataDto {
		const { v, yr } = this.parseAgValue(this.selectedScrapedAg());
		return {
			rank: ranking.rank, team: ranking.team, state: ranking.state,
			record: ranking.record, rating: ranking.rating, agd: ranking.agd,
			sched: ranking.sched, matchScore, matchedAt: new Date().toISOString(),
			season: yr || null, rankingSource: v || null
		};
	}

	/** The stamp currently in the database for this team, from the saved-state read. */
	private storedRankingFor(teamId: string): NationalRankingDataDto | null {
		const team = this.agegroupTeams().find(t => t.teamId === teamId);
		return this.parseRankingData(team?.nationalRankingData);
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

	/**
	 * The stamp stored on an UNPAIRED row, so the table can show a previously saved rank before
	 * any scrape has run — and keep showing it for a team this scrape failed to re-match. Returns
	 * null for matched rows, where the live pairing is what the columns should be reporting.
	 */
	storedOnly(row: MasterRow): NationalRankingDataDto | null {
		return row.match ? null : this.parseRankingData(row.team.nationalRankingData);
	}

	/** Which dropdowns are live. The national pair means nothing without an age group of ours. */
	readonly canPickSeason = computed(() => !!this.selectedRegisteredAg());
	readonly canPickNationalAg = computed(() =>
		!!this.selectedRegisteredAg() && !!this.selectedSeason() && this.scrapedAgeGroups().length > 0);

	// ── Rename a local team (pencil) ──

	openRename(team: RankingsTeamDto): void {
		this.renameError.set(null);
		this.renamingTeam.set(team);
	}

	closeRename(): void {
		this.renamingTeam.set(null);
		this.renameBusy.set(false);
		this.renameError.set(null);
	}

	/**
	 * Renames THIS EVENT's copy only — `EditTeamRequest.TeamName` on the AdminOnly team-search
	 * endpoint. The club's library entry and every other job the team plays in keep their name;
	 * there is no fan-out and no option to ask for one.
	 *
	 * Deliberately NOT the rep's `PUT /teams/{teamId}/rename`: that route is `IsClubRepRole()`-gated
	 * and would 403 every director, SuperDirector and SuperUser — i.e. everyone who can reach this
	 * screen.
	 *
	 * The dialog stays open on failure so the director can fix the name they typed rather than
	 * watch it disappear behind a toast.
	 */
	confirmRename(confirmation: TeamRenameConfirmation): void {
		const team = this.renamingTeam();
		if (!team) return;

		const name = confirmation.name.trim();
		if (!name || name === team.teamName) { this.closeRename(); return; }

		this.renameBusy.set(true);
		this.renameError.set(null);

		this.teamSearchService.editTeam(team.teamId, { teamName: name })
			.pipe(takeUntilDestroyed(this.destroyRef))
			.subscribe({
				next: () => {
					this.applyRenameLocally(team.teamId, name);
					this.closeRename();
					this.successMessage.set(`Renamed to ${name} for this event only.`);
					// The name is the matcher's main input, and fixing it is usually how a
					// director unblocks a pairing the old name was hiding. Re-check this one
					// team rather than making them re-run the whole match and lose their
					// hand corrections.
					this.reassessAfterRename(team.teamId, name);
				},
				error: (err: unknown) => {
					this.renameBusy.set(false);
					this.renameError.set(extractHttpErrorMessage(err, 'Failed to rename team.'));
				}
			});
	}

	/**
	 * Re-score the renamed team against the rankings still unpaired, and take the result if the
	 * matcher finds one. Applied rather than proposed: every row on this screen is already
	 * reviewable and undoable before Save, and a confirm on every rename would wear thin fast.
	 * The row is flagged `reassigned` so it is obvious which pairing the rename produced.
	 *
	 * Only meaningful once a match has been run — with nothing scraped there is nothing to score
	 * against, so the rename simply stands on its own.
	 */
	private reassessAfterRename(teamId: string, newName: string): void {
		const registered = this.selectedRegisteredAg();
		const candidates = this.unmatchedRankings();
		if (!registered || !this.hasResults() || candidates.length === 0) return;

		// Only teams with no pairing are worth re-checking. Re-scoring a team the director has
		// already matched would silently overwrite a decision they made.
		const isUnmatched = this.unmatchedTeams().some(t => t.teamId === teamId);
		if (!isUnmatched) return;

		this.isReassessing.set(teamId);

		this.rankingsService.reassessTeam({
			teamId,
			registeredTeamAgeGroupId: registered,
			candidateRankings: candidates,
			clubWeight: 75,
			teamWeight: 25
		}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: result => {
				this.isReassessing.set(null);
				const match = result.match;
				if (!match) {
					this.successMessage.set(
						`Renamed to ${newName} for this event only. `
						+ 'No ranking matched the new name — pair it by hand if you know the right one.');
					return;
				}
				// Move the team out of the unmatched list and claim its ranking, exactly as a
				// hand match does.
				this.matchedTeams.set([...this.matchedTeams(), match]);
				this.unmatchedTeams.set(this.unmatchedTeams().filter(t => t.teamId !== teamId));
				this.unmatchedRankings.set(
					this.unmatchedRankings().filter(r => r.rank !== match.ranking.rank));
				this.reassessedTeamIds.set(new Set([...this.reassessedTeamIds(), teamId]));
				this.successMessage.set(
					`Renamed to ${newName} — now matched to #${match.ranking.rank} `
					+ `${match.ranking.team} at ${this.formatPercent(match.matchScore)}. `
					+ 'Save to keep it.');
			},
			// A failed re-check must not read as a failed rename: the rename already committed.
			error: () => {
				this.isReassessing.set(null);
				this.successMessage.set(
					`Renamed to ${newName} for this event only. `
					+ "Couldn't re-check it against the rankings — use Look Up Again when you're ready.");
			}
		});
	}

	/** Patch every in-memory copy of the row — matched, unmatched, and the saved-state read. */
	private applyRenameLocally(teamId: string, teamName: string): void {
		const rename = <T extends RankingsTeamDto>(t: T): T =>
			t.teamId === teamId ? { ...t, teamName } : t;

		this.matchedTeams.set(this.matchedTeams().map(m =>
			m.registeredTeam.teamId === teamId
				? { ...m, registeredTeam: rename(m.registeredTeam) }
				: m));
		this.unmatchedTeams.set(this.unmatchedTeams().map(rename));
		this.agegroupTeams.set(this.agegroupTeams().map(rename));
	}

}
