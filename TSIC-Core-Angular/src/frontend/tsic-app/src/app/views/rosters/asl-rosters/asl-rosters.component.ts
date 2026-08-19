import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AslRegionTeamDto, AslTeamMenuItemDto } from '@core/api';
import { AslRosterService } from './asl-roster.service';

/**
 * Public ASL roster board — ported from the legacy ASLRosters/Index MVC view.
 *
 * The director links this in from americanselectlacrosse.com and screenshots it straight into
 * social feeds, so the card layout is a fixed brand artifact: it is NOT palette-responsive and
 * must not shift when the job's color palette changes. Brand values live in the component SCSS.
 */
@Component({
	selector: 'app-asl-rosters',
	standalone: true,
	templateUrl: './asl-rosters.component.html',
	styleUrls: ['./asl-rosters.component.scss'],
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class AslRostersComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly svc = inject(AslRosterService);

	private jobPath = '';

	regions = signal<string[]>([]);
	allTeams = signal<AslTeamMenuItemDto[]>([]);

	selectedRegion = signal('');
	cards = signal<AslRegionTeamDto[]>([]);

	isLoading = signal(false);
	errorMessage = signal('');

	/** Heading above the cards — the region with its "ASL:" prefix stripped. */
	regionHeading = computed(() => this.stripPrefix(this.selectedRegion()));

	/** Team dropdown is hidden until a region is picked, then scoped to that region. */
	regionTeams = computed<AslTeamMenuItemDto[]>(() => {
		const heading = this.regionHeading();
		if (!heading) return [];
		return this.allTeams().filter(t => this.stripPrefix(t.teamName).includes(heading));
	});

	ngOnInit(): void {
		this.jobPath = this.route.snapshot.params['jobPath']
			?? this.route.parent?.snapshot.params['jobPath']
			?? '';

		this.svc.getIndex(this.jobPath).subscribe({
			next: data => {
				this.regions.set(data.regions);
				this.allTeams.set(data.teams);

				// Legacy honored ?region= on load so the director could deep-link one region.
				const qsRegion = this.route.snapshot.queryParamMap.get('region');
				if (qsRegion) {
					this.selectedRegion.set(qsRegion);
					this.loadRegion(qsRegion);
				}
			},
			error: () => this.errorMessage.set('Rosters are not available for this event.')
		});
	}

	onRegionChange(region: string): void {
		this.selectedRegion.set(region);
		this.cards.set([]);
		if (region) this.loadRegion(region);
	}

	onTeamChange(teamId: string): void {
		if (!teamId) {
			// Back to "--select your TEAM--" — restore the whole region.
			if (this.selectedRegion()) this.loadRegion(this.selectedRegion());
			return;
		}

		this.isLoading.set(true);
		this.svc.getTeamRoster(teamId, this.jobPath).subscribe({
			next: card => {
				this.cards.set([card]);
				this.isLoading.set(false);
			},
			error: () => {
				this.errorMessage.set('That roster could not be loaded.');
				this.isLoading.set(false);
			}
		});
	}

	private loadRegion(region: string): void {
		this.isLoading.set(true);
		this.errorMessage.set('');
		this.svc.getRegionRoster(region, this.jobPath).subscribe({
			next: cards => {
				this.cards.set(cards);
				this.isLoading.set(false);
			},
			error: () => {
				this.errorMessage.set('That region could not be loaded.');
				this.isLoading.set(false);
			}
		});
	}

	/** "ASL:Long Island Blue 2029" → "Long Island Blue 2029". Legacy did name.split(':')[1]. */
	stripPrefix(name: string): string {
		const idx = name.indexOf(':');
		return idx >= 0 ? name.slice(idx + 1).trim() : name.trim();
	}

	/** Schools arrive upper-cased from registration; legacy title-cased them for display. */
	toTitleCase(value: string | null | undefined): string {
		if (!value) return '';
		return value.toLowerCase().split(' ')
			.map(w => w.charAt(0).toUpperCase() + w.slice(1))
			.join(' ');
	}
}
