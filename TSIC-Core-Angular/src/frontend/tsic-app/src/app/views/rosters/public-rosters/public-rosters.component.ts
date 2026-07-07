import { Component, ChangeDetectionStrategy, inject, signal, computed, ElementRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ListBoxModule, FilteringEventArgs } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { CadtClubNode, CadtTeamNode, PublicRosterPlayerDto, TeamResultDto } from '@core/api';
import { PublicRosterService } from './public-roster.service';
import { ViewScheduleService } from '../../scheduling/view-schedule/services/view-schedule.service';
import { TeamResultsModalComponent } from '../../scheduling/view-schedule/components/team-results-modal.component';

/** Flat team entry for search results — carries breadcrumb context. */
interface FlatTeam {
	team: CadtTeamNode;
	clubName: string;
	agegroupName: string;
	divName: string;
	color: string | null;
}

/** Row shape bound to the Syncfusion ListBox (text/value/groupBy fields). */
interface TeamListItem {
	teamId: string;
	clubName: string;
	agegroupName: string;
	label: string;
}

/** Presentation model for a roster row — name cleaned of the "Staff:" prefix. */
interface RosterRow {
	name: string;
	position: string;
	uniformNo: string;
	isStaff: boolean;
}

@Component({
	selector: 'app-public-rosters',
	standalone: true,
	imports: [CommonModule, ListBoxModule, TeamResultsModalComponent],
	templateUrl: './public-rosters.component.html',
	styleUrls: ['./public-rosters.component.scss'],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class PublicRostersComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly svc = inject(PublicRosterService);
	private readonly scheduleSvc = inject(ViewScheduleService);
	private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

	clubs = signal<CadtClubNode[]>([]);
	rosterPlayers = signal<PublicRosterPlayerDto[]>([]);
	selectedTeamId = signal<string | null>(null);
	selectedTeamName = signal('');
	selectedAgegroupName = signal('');
	selectedDivName = signal('');
	selectedColor = signal<string | null>(null);
	isLoadingTree = signal(false);
	isLoadingRoster = signal(false);
	errorMessage = signal('');

	// Whether the event restricts public rosters (Jobs.bRestrictPublicRosters) — shows a "not available" notice.
	restricted = signal(false);

	// Pre-built flat team index — source for both the ListBox and roster lookup.
	allTeams = signal<FlatTeam[]>([]);

	// Whether the event allows public schedule viewing
	schedulePublic = signal(false);

	// Team results modal
	teamResultsVisible = signal(false);
	teamResultsName = signal('');
	teamResults = signal<TeamResultDto[]>([]);

	private jobPath = '';

	/** Active sort column for the roster table; null = default (name ascending). */
	readonly sortColumn = signal<'uniform' | 'name' | 'position' | null>(null);
	readonly sortDir = signal<'asc' | 'desc'>('asc');

	/** Header click: same column toggles direction, new column starts ascending. */
	toggleSort(col: 'uniform' | 'name' | 'position'): void {
		if (this.sortColumn() === col) {
			this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
		} else {
			this.sortColumn.set(col);
			this.sortDir.set('asc');
		}
	}

	/** aria-sort value for a header cell. */
	ariaSort(col: 'uniform' | 'name' | 'position'): 'ascending' | 'descending' | 'none' {
		if (this.sortColumn() !== col) return 'none';
		return this.sortDir() === 'asc' ? 'ascending' : 'descending';
	}

	/**
	 * Compare two rows by the active sort. Name is the tiebreaker everywhere.
	 * Blank uniform / position always sort last (regardless of direction) so
	 * empty cells don't lead the list.
	 */
	private compareRows(a: RosterRow, b: RosterRow): number {
		const col = this.sortColumn();
		const dir = this.sortDir() === 'desc' ? -1 : 1;
		if (col === 'uniform') {
			const an = parseInt(a.uniformNo, 10);
			const bn = parseInt(b.uniformNo, 10);
			const aHas = !isNaN(an), bHas = !isNaN(bn);
			if (!aHas && !bHas) return a.name.localeCompare(b.name);
			if (!aHas) return 1;
			if (!bHas) return -1;
			return dir * (an - bn) || a.name.localeCompare(b.name);
		}
		if (col === 'position') {
			const av = a.position.trim(), bv = b.position.trim();
			if (!av && !bv) return a.name.localeCompare(b.name);
			if (!av) return 1;
			if (!bv) return -1;
			return dir * av.localeCompare(bv) || a.name.localeCompare(b.name);
		}
		// col === 'name' or default (null)
		return dir * a.name.localeCompare(b.name);
	}

	/**
	 * Roster rows for display. Staff are ALWAYS pinned below players (so the first
	 * row under the "# / Player / Position" headers is a player); the active column
	 * sort is applied within each group. `RoleLabel` is reliably "Player"/"Staff";
	 * the "Staff:" name prefix baked in by the backend is stripped here so the badge
	 * carries that signal instead.
	 */
	readonly sortedRoster = computed<RosterRow[]>(() => {
		const rows = this.rosterPlayers().map(p => {
			const isStaff = p.roleLabel === 'Staff';
			return {
				name: isStaff ? p.displayName.replace(/^Staff:\s*/i, '') : p.displayName,
				position: p.position ?? '',
				uniformNo: p.uniformNo ?? '',
				isStaff,
			};
		});
		const players = rows.filter(r => !r.isStaff).sort((a, b) => this.compareRows(a, b));
		const staff = rows.filter(r => r.isStaff).sort((a, b) => this.compareRows(a, b));
		return [...players, ...staff];
	});

	/** Player / staff tallies for the roster summary line. */
	readonly rosterCounts = computed(() => {
		const rows = this.sortedRoster();
		const staff = rows.filter(r => r.isStaff).length;
		return { players: rows.length - staff, staff };
	});

	/**
	 * True when the event actually spans multiple clubs. Drives grouping, the club
	 * prefix in each row, and the page title — single-club / house events have no
	 * meaningful club axis.
	 */
	readonly hasClubs = computed(() => new Set(this.allTeams().map(ft => ft.clubName)).size > 1);

	/** Group by club when the event spans clubs; otherwise by agegroup. */
	readonly groupField = computed<'clubName' | 'agegroupName'>(() =>
		this.hasClubs() ? 'clubName' : 'agegroupName'
	);

	/** Page heading — reflects whether the event is club- or agegroup-organized. */
	readonly pageTitle = computed(() => this.hasClubs() ? 'Club Team Rosters' : 'Age Group Team Rosters');

	/** Filter placeholder — omits "club" when the event has no club axis. */
	readonly filterPlaceholder = computed(() =>
		this.hasClubs() ? 'Search by club, age group, or team…' : 'Search by age group or team…'
	);

	/** Syncfusion ListBox field map — groupBy renders section headers. */
	readonly listBoxFields = computed(() => ({
		text: 'label',
		value: 'teamId',
		groupBy: this.groupField(),
	}));

	/** Single-select picker (no checkboxes) — one team loads one roster. */
	readonly listBoxSelection = { mode: 'Single' as const, showCheckbox: false };

	/**
	 * Flat list shaped for the ListBox. The label concatenates every searchable
	 * field — club (only for multi-club events), agegroup, team, roster count — so
	 * the custom "contains" filter matches any of them anywhere. Sorted by the
	 * active group field first so groups stay contiguous.
	 */
	readonly listBoxData = computed<TeamListItem[]>(() => {
		const group = this.groupField();
		const withClub = this.hasClubs();
		return this.allTeams()
			.map(ft => ({
				teamId: ft.team.teamId,
				clubName: ft.clubName,
				agegroupName: ft.agegroupName,
				label: `${withClub ? `${ft.clubName} — ` : ''}${ft.agegroupName}:${ft.team.teamName} (${ft.team.playerCount})`,
			}))
			.sort((a, b) =>
				(group === 'clubName'
					? a.clubName.localeCompare(b.clubName)
					: a.agegroupName.localeCompare(b.agegroupName))
				|| a.label.localeCompare(b.label)
			);
	});

	ngOnInit(): void {
		this.jobPath = this.route.snapshot.params['jobPath']
			?? this.route.parent?.snapshot.params['jobPath']
			?? '';
		this.loadTree();
	}

	private loadTree(): void {
		this.isLoadingTree.set(true);
		this.errorMessage.set('');
		this.svc.getTree(this.jobPath).subscribe({
			next: (data) => {
				this.restricted.set(data.restrictPublicRosters ?? false);
				const clubs = data.clubs ?? [];
				this.clubs.set(clubs);
				this.allTeams.set(this.buildFlatIndex(clubs));
				this.schedulePublic.set(data.schedulePublic ?? false);
				this.isLoadingTree.set(false);
			},
			error: () => {
				this.isLoadingTree.set(false);
				this.errorMessage.set('Failed to load rosters.');
			}
		});
	}

	private buildFlatIndex(clubs: CadtClubNode[]): FlatTeam[] {
		const result: FlatTeam[] = [];
		for (const c of clubs) {
			for (const ag of c.agegroups) {
				for (const d of ag.divisions) {
					for (const t of d.teams) {
						result.push({
							team: t,
							clubName: c.clubName,
							agegroupName: ag.agegroupName,
							divName: d.divName,
							color: ag.color ?? null
						});
					}
				}
			}
		}
		result.sort((a, b) =>
			a.clubName.localeCompare(b.clubName) || a.team.teamName.localeCompare(b.team.teamName)
		);
		return result;
	}

	private selectTeamById(teamId: string): void {
		if (this.selectedTeamId() === teamId) return;
		this.selectedTeamId.set(teamId);
		this.isLoadingRoster.set(true);
		this.svc.getTeamRoster(teamId, this.jobPath).subscribe({
			next: (players) => {
				this.rosterPlayers.set(players);
				this.isLoadingRoster.set(false);
			},
			error: () => {
				this.rosterPlayers.set([]);
				this.isLoadingRoster.set(false);
			}
		});
	}

	private selectFlatTeam(ft: FlatTeam): void {
		this.selectedTeamName.set(`${ft.clubName} — ${ft.team.teamName}`);
		this.selectedAgegroupName.set(ft.agegroupName);
		this.selectedDivName.set(ft.divName);
		this.selectedColor.set(ft.color);
		this.selectTeamById(ft.team.teamId);
	}

	/** Focus the ListBox filter bar on render so users can type immediately. */
	onListBoxCreated(): void {
		// The filter input exists at `created`, but a synchronous focus() during the
		// render cycle is dropped by the browser — defer to after the next paint.
		requestAnimationFrame(() =>
			this.host.nativeElement.querySelector<HTMLInputElement>('.e-input-filter')?.focus()
		);
	}

	/**
	 * Custom typeahead — case-insensitive "contains" against the whole label, so a
	 * match on any part (club, agegroup, team, or count) anywhere in the string
	 * surfaces the row. Default ListBox filtering only prefix-matches.
	 */
	onFiltering(e: FilteringEventArgs): void {
		const text = e.text?.trim();
		const query = text
			? new Query().where('label', 'contains', text, true)
			: new Query();
		e.updateData(this.listBoxData() as unknown as { [key: string]: object }[], query);
	}

	/** ListBox selection changed — map the picked value back to its FlatTeam. */
	onTeamSelect(value: string[] | string | null | undefined): void {
		const teamId = Array.isArray(value) ? value[0] : value;
		if (!teamId) return;
		const ft = this.allTeams().find(x => x.team.teamId === teamId);
		if (ft) this.selectFlatTeam(ft);
	}

	viewTeamSchedule(teamId: string): void {
		this.teamResultsVisible.set(true);
		this.teamResultsName.set('');
		this.scheduleSvc.getTeamResults(teamId, this.jobPath).subscribe(response => {
			this.teamResults.set(response.games);
			const parts = [response.agegroupName, response.clubName, response.teamName].filter(Boolean);
			this.teamResultsName.set(parts.join(' — '));
		});
	}

	/** Return white or dark text for WCAG contrast against a hex background. */
	contrastText(hex: string | null): string {
		if (!hex || !hex.startsWith('#')) return '#fff';
		const r = parseInt(hex.slice(1, 3), 16);
		const g = parseInt(hex.slice(3, 5), 16);
		const b = parseInt(hex.slice(5, 7), 16);
		// Relative luminance (sRGB)
		const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
		return lum > 0.55 ? '#1c1917' : '#fff';
	}
}
