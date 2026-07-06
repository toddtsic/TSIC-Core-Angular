import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { ListBoxModule } from '@syncfusion/ej2-angular-dropdowns';
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

@Component({
	selector: 'app-public-rosters',
	standalone: true,
	imports: [CommonModule, GridAllModule, ListBoxModule, TeamResultsModalComponent],
	templateUrl: './public-rosters.component.html',
	styleUrls: ['./public-rosters.component.scss'],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class PublicRostersComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly svc = inject(PublicRosterService);
	private readonly scheduleSvc = inject(ViewScheduleService);

	clubs = signal<CadtClubNode[]>([]);
	rosterPlayers = signal<PublicRosterPlayerDto[]>([]);
	rosterSortSettings: SortSettingsModel = { columns: [{ field: 'displayName', direction: 'Ascending' }] };
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

	/**
	 * Group by club when the event actually spans multiple clubs; otherwise group
	 * by agegroup (single-club / house events, where every team shares one club
	 * name and a club header would be meaningless).
	 */
	readonly groupField = computed<'clubName' | 'agegroupName'>(() => {
		const distinctClubs = new Set(this.allTeams().map(ft => ft.clubName));
		return distinctClubs.size > 1 ? 'clubName' : 'agegroupName';
	});

	/** Syncfusion ListBox field map — groupBy renders section headers. */
	readonly listBoxFields = computed(() => ({
		text: 'label',
		value: 'teamId',
		groupBy: this.groupField(),
	}));

	/** Single-select picker (no checkboxes) — one team loads one roster. */
	readonly listBoxSelection = { mode: 'Single' as const, showCheckbox: false };

	/**
	 * Flat list shaped for the ListBox. The label carries club + team so the
	 * built-in filter bar (which matches the text field) works as a typeahead for
	 * either. Sorted by the active group field first so groups stay contiguous.
	 */
	readonly listBoxData = computed<TeamListItem[]>(() => {
		const group = this.groupField();
		return this.allTeams()
			.map(ft => ({
				teamId: ft.team.teamId,
				clubName: ft.clubName,
				agegroupName: ft.agegroupName,
				label: `${ft.clubName} — ${ft.team.teamName} (${ft.team.playerCount})`,
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
