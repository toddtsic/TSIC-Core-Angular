import { Component, ChangeDetectionStrategy, signal, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BracketSeedService } from './services/bracket-seed.service';
import { ViewScheduleService } from '../view-schedule/services/view-schedule.service';
import type {
	BracketSeedGameDto,
	BracketSeedDivisionOptionDto,
	UpdateBracketSeedRequest,
	StandingsByDivisionResponse,
} from '@core/api';

@Component({
	selector: 'app-bracket-seeds',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './bracket-seeds.component.html',
	styleUrl: './bracket-seeds.component.scss',
})
export class BracketSeedsComponent implements OnInit {
	private readonly svc = inject(BracketSeedService);
	private readonly viewScheduleSvc = inject(ViewScheduleService);

	// Bracket games
	bracketGames = signal<BracketSeedGameDto[]>([]);
	isLoading = signal(true);
	errorMessage = signal('');

	// Edit state
	editingGame = signal<BracketSeedGameDto | null>(null);
	divisionOptions = signal<BracketSeedDivisionOptionDto[]>([]);
	isLoadingDivisions = signal(false);
	isSaving = signal(false);
	editT1DivId = signal<string>('');
	editT1Rank = signal<number | null>(null);
	editT2DivId = signal<string>('');
	editT2Rank = signal<number | null>(null);

	// Standings
	standingsData = signal<StandingsByDivisionResponse | null>(null);
	isLoadingStandings = signal(false);
	showStandings = signal(true);

	rankOptions = Array.from({ length: 12 }, (_, i) => i + 1);

	ngOnInit(): void {
		this.loadBracketGames();
		this.loadStandings();
	}

	private loadBracketGames(): void {
		this.isLoading.set(true);
		this.errorMessage.set('');
		this.svc.getBracketGames().subscribe({
			next: (games) => {
				this.bracketGames.set(games);
				this.isLoading.set(false);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message || 'Failed to load bracket games');
				this.isLoading.set(false);
			},
		});
	}

	private loadStandings(): void {
		this.isLoadingStandings.set(true);
		this.viewScheduleSvc.getStandings({}).subscribe({
			next: (data) => {
				this.standingsData.set(data);
				this.isLoadingStandings.set(false);
			},
			error: () => this.isLoadingStandings.set(false),
		});
	}

	openEdit(game: BracketSeedGameDto): void {
		this.editingGame.set(game);
		this.editT1DivId.set(game.t1SeedDivId ?? '');
		this.editT1Rank.set(game.t1SeedRank ?? null);
		this.editT2DivId.set(game.t2SeedDivId ?? '');
		this.editT2Rank.set(game.t2SeedRank ?? null);

		this.isLoadingDivisions.set(true);
		this.svc.getDivisionsForGame(game.gid).subscribe({
			next: (divs) => {
				this.divisionOptions.set(divs);
				this.isLoadingDivisions.set(false);
			},
			error: () => this.isLoadingDivisions.set(false),
		});
	}

	cancelEdit(): void {
		this.editingGame.set(null);
		this.divisionOptions.set([]);
	}

	saveEdit(): void {
		const game = this.editingGame();
		if (!game) return;

		this.isSaving.set(true);
		const request: UpdateBracketSeedRequest = {
			gid: game.gid,
			t1SeedDivId: this.editT1DivId() || undefined,
			t1SeedRank: this.editT1Rank() ?? undefined,
			t2SeedDivId: this.editT2DivId() || undefined,
			t2SeedRank: this.editT2Rank() ?? undefined,
		};

		this.svc.updateSeed(request).subscribe({
			next: (updated) => {
				this.bracketGames.set(
					this.bracketGames().map((g) => (g.gid === updated.gid ? updated : g))
				);
				this.isSaving.set(false);
				this.editingGame.set(null);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message || 'Failed to save');
				this.isSaving.set(false);
			},
		});
	}

	showT1(game: BracketSeedGameDto): boolean {
		return game.whichSide !== 2;
	}

	showT2(game: BracketSeedGameDto): boolean {
		return game.whichSide !== 1;
	}

	toggleStandings(): void {
		this.showStandings.set(!this.showStandings());
	}
}
