import { Component, ChangeDetectionStrategy, inject, signal, viewChild, ElementRef, afterNextRender } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
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

@Component({
	selector: 'app-public-rosters',
	standalone: true,
	imports: [CommonModule, TeamResultsModalComponent],
	templateUrl: './public-rosters.component.html',
	styleUrls: ['./public-rosters.component.scss'],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class PublicRostersComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly svc = inject(PublicRosterService);
	private readonly scheduleSvc = inject(ViewScheduleService);
	private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

	constructor() {
		afterNextRender(() => this.searchInput()?.nativeElement.focus());
	}

	clubs = signal<CadtClubNode[]>([]);
	rosterPlayers = signal<PublicRosterPlayerDto[]>([]);
	sortColumn = signal<string>('displayName');
	sortAsc = signal(true);
	selectedTeamId = signal<string | null>(null);
	selectedTeamName = signal('');
	selectedAgegroupName = signal('');
	selectedDivName = signal('');
	selectedColor = signal<string | null>(null);
	isLoadingTree = signal(false);
	isLoadingRoster = signal(false);
	errorMessage = signal('');
	searchText = signal('');

	// Pre-built flat team index — always visible, filtered by search
	allTeams = signal<FlatTeam[]>([]);

	// Whether the event allows public schedule viewing
	schedulePublic = signal(false);

	// Team results modal
	teamResultsVisible = signal(false);
	teamResultsName = signal('');
	teamResults = signal<TeamResultDto[]>([]);

	private jobPath = '';

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

	/** Filter teams by search text. Empty search = no results (user must type). */
	filteredTeams(): FlatTeam[] {
		const q = this.searchText().toLowerCase().trim();
		if (!q) return [];
		return this.allTeams().filter(ft =>
			ft.team.teamName.toLowerCase().includes(q) ||
			ft.clubName.toLowerCase().includes(q) ||
			ft.agegroupName.toLowerCase().includes(q)
		);
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

	selectFlatTeam(ft: FlatTeam): void {
		this.selectedTeamName.set(`${ft.clubName} — ${ft.team.teamName}`);
		this.selectedAgegroupName.set(ft.agegroupName);
		this.selectedDivName.set(ft.divName);
		this.selectedColor.set(ft.color);
		this.selectTeamById(ft.team.teamId);
	}

	onSearchChange(event: Event): void {
		this.searchText.set((event.target as HTMLInputElement).value);
	}

	clearSearch(): void {
		this.searchText.set('');
	}

	toggleSort(column: string): void {
		if (this.sortColumn() === column) {
			this.sortAsc.set(!this.sortAsc());
		} else {
			this.sortColumn.set(column);
			this.sortAsc.set(true);
		}
	}

	sortedPlayers(): PublicRosterPlayerDto[] {
		const players = [...this.rosterPlayers()];
		const col = this.sortColumn();
		const asc = this.sortAsc();
		const dir = asc ? 1 : -1;

		return players.sort((a, b) => {
			let va: string | number = '';
			let vb: string | number = '';

			switch (col) {
				case 'displayName':
					va = a.displayName; vb = b.displayName; break;
				case 'position':
					va = a.position ?? ''; vb = b.position ?? ''; break;
				case 'uniformNo':
					va = parseInt(a.uniformNo ?? '999', 10);
					vb = parseInt(b.uniformNo ?? '999', 10);
					return (va - vb) * dir;
				case 'clubName':
					va = a.clubName ?? ''; vb = b.clubName ?? ''; break;
			}

			return (va < vb ? -1 : va > vb ? 1 : 0) * dir;
		});
	}

	sortIcon(column: string): string {
		if (this.sortColumn() !== column) return 'bi-chevron-expand';
		return this.sortAsc() ? 'bi-sort-up' : 'bi-sort-down';
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
