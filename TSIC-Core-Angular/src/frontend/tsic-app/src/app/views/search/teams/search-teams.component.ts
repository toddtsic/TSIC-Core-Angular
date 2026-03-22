import { Component, OnInit, OnDestroy, ViewChild, signal, computed, inject, ChangeDetectionStrategy, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent, PageSettingsModel, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { MultiSelectModule, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';

import { TeamSearchService } from './services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { TeamDetailPanelComponent } from './components/team-detail-panel.component';
import { LadtTreeFilterComponent } from '../players/components/ladt-tree-filter.component';
import { CadtTreeFilterComponent } from '@shared/components/cadt-tree-filter/cadt-tree-filter.component';

import type {
	TeamSearchRequest,
	TeamSearchResponse,
	TeamFilterOptionsDto,
	TeamSearchDetailDto,
	AccountingRecordDto,
	FilterOption,
	LadtTreeNodeDto,
	CadtClubNode
} from '@core/api';

interface FilterChip {
	category: string;
	label: string;
	filterKey: keyof TeamSearchRequest;
	value: string;
}

@Component({
	selector: 'app-search-teams',
	standalone: true,
	imports: [
		CommonModule,
		FormsModule,
		GridAllModule,
		MultiSelectModule,
		TeamDetailPanelComponent,
		LadtTreeFilterComponent,
		CadtTreeFilterComponent
	],
	schemas: [CUSTOM_ELEMENTS_SCHEMA],
	providers: [CheckBoxSelectionService],
	templateUrl: './search-teams.component.html',
	styleUrl: './search-teams.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamSearchComponent implements OnInit, OnDestroy {
	private readonly searchService = inject(TeamSearchService);
	private readonly toast = inject(ToastService);

	@ViewChild('grid') grid!: GridComponent;

	// Filter options
	filterOptions = signal<TeamFilterOptionsDto | null>(null);

	// LADT tree state
	ladtTree = signal<LadtTreeNodeDto[]>([]);
	ladtTreeLoading = signal(true);
	ladtCheckedIds = signal<Set<string>>(new Set());

	// CADT tree state
	cadtTree = signal<CadtClubNode[]>([]);
	cadtTreeLoading = signal(true);
	cadtCheckedIds = signal<Set<string>>(new Set());
	hasCadtData = computed(() => this.cadtTree().length > 0);
	jobName = computed(() => this.ladtTree()[0]?.name ?? '');

	// Search state
	searchRequest = signal<TeamSearchRequest>({
		clubNames: [],
		levelOfPlays: [],
		agegroupIds: [],
		divisionIds: [],
		teamIds: [],
		activeStatuses: ['True'],
		payStatuses: [],
		cadtTeamIds: []
	});

	searchResults = signal<TeamSearchResponse | null>(null);
	isSearching = signal(false);

	// Dirty-state tracking
	private lastSearchedRequest = signal<string | null>(null);
	filtersAreDirty = computed(() => {
		const last = this.lastSearchedRequest();
		if (last === null) return false;
		return JSON.stringify(this.sanitizeRequest(this.searchRequest())) !== last;
	});

	// Detail panel state
	selectedDetail = signal<TeamSearchDetailDto | null>(null);
	isPanelOpen = signal(false);

	// Grid configuration
	pageSettings: PageSettingsModel = { pageSize: 20, pageSizes: [20, 50, 100, 'All'] };
	sortSettings: SortSettingsModel = { columns: [{ field: 'clubName', direction: 'Ascending' }] };

	// Syncfusion MultiSelect fields
	msFields = { value: 'value', text: 'text' };

	// Filter chips
	activeFilterChips = computed<FilterChip[]>(() => {
		const req = this.searchRequest();
		const opts = this.filterOptions();
		const chips: FilterChip[] = [];

		const addArrayChips = (
			category: string,
			filterKey: keyof TeamSearchRequest,
			values: any[] | null | undefined,
			options?: FilterOption[]
		) => {
			if (!values?.length) return;
			for (const val of values) {
				const label = options?.find(o => o.value === String(val))?.text ?? String(val);
				chips.push({ category, label, filterKey, value: String(val) });
			}
		};

		addArrayChips('Club', 'clubNames', req.clubNames, opts?.clubs);
		addArrayChips('LOP', 'levelOfPlays', req.levelOfPlays, opts?.levelOfPlays);
		addArrayChips('Status', 'activeStatuses', req.activeStatuses, opts?.activeStatuses);
		addArrayChips('Pay', 'payStatuses', req.payStatuses, opts?.payStatuses);

		// LADT tree chips
		const treeCheckedIds = this.ladtCheckedIds();
		if (treeCheckedIds.size > 0) {
			const levelConfig: Record<number, { category: string; filterKey: keyof TeamSearchRequest }> = {
				1: { category: 'Agegroup', filterKey: 'agegroupIds' },
				2: { category: 'Division', filterKey: 'divisionIds' },
				3: { category: 'Team', filterKey: 'teamIds' }
			};
			const walkForChips = (nodes: LadtTreeNodeDto[], ancestorChecked: boolean) => {
				for (const node of nodes) {
					const isChecked = treeCheckedIds.has(node.id);
					if (isChecked && !ancestorChecked && node.level >= 1) {
						const cfg = levelConfig[node.level];
						if (cfg) chips.push({ category: cfg.category, label: node.name, filterKey: cfg.filterKey, value: node.id });
					}
					const children = (node.children ?? []) as LadtTreeNodeDto[];
					if (children.length > 0) walkForChips(children, ancestorChecked || (isChecked && node.level >= 1));
				}
			};
			walkForChips(this.ladtTree(), false);
		}

		// CADT tree chips — highest-level checked ancestor only
		const cadtChecked = this.cadtCheckedIds();
		if (cadtChecked.size > 0 && this.cadtTree().length > 0) {
			for (const club of this.cadtTree()) {
				const clubId = `club:${club.clubName}`;
				if (cadtChecked.has(clubId)) {
					chips.push({ category: 'Club Team', label: club.clubName, filterKey: 'cadtTeamIds', value: clubId });
					continue;
				}
				for (const ag of club.agegroups ?? []) {
					const agId = `ag:${club.clubName}|${ag.agegroupId}`;
					if (cadtChecked.has(agId)) {
						chips.push({ category: 'Club AG', label: `${club.clubName} › ${ag.agegroupName}`, filterKey: 'cadtTeamIds', value: agId });
						continue;
					}
					for (const div of ag.divisions ?? []) {
						const divId = `div:${club.clubName}|${div.divId}`;
						if (cadtChecked.has(divId)) {
							chips.push({ category: 'Club Div', label: `${club.clubName} › ${div.divName}`, filterKey: 'cadtTeamIds', value: divId });
							continue;
						}
						for (const team of div.teams ?? []) {
							const teamId = `team:${team.teamId}`;
							if (cadtChecked.has(teamId)) {
								chips.push({ category: 'Club Team', label: team.teamName, filterKey: 'cadtTeamIds', value: teamId });
							}
						}
					}
				}
			}
		}

		return chips;
	});

	// Resize handler
	private resizeHandler = () => {};

	ngOnInit(): void {
		this.loadFilterOptions();
		this.loadLadtTree();
		this.loadCadtTree();
	}

	ngOnDestroy(): void {
		window.removeEventListener('resize', this.resizeHandler);
	}

	loadFilterOptions(): void {
		this.searchService.getFilterOptions().subscribe({
			next: (options) => {
				this.filterOptions.set(options);
				this.lastSearchedRequest.set(JSON.stringify(this.sanitizeRequest(this.searchRequest())));
			},
			error: (err) => {
				this.toast.show('Failed to load filter options', 'danger', 4000);
				console.error('Error loading filter options:', err);
			}
		});
	}

	private loadLadtTree(): void {
		this.ladtTreeLoading.set(true);
		this.searchService.getLadtTree().subscribe({
			next: (root) => {
				this.ladtTree.set(root.leagues as LadtTreeNodeDto[]);
				this.ladtTreeLoading.set(false);
			},
			error: (err) => {
				console.error('Error loading LADT tree:', err);
				this.ladtTreeLoading.set(false);
			}
		});
	}

	private loadCadtTree(): void {
		this.cadtTreeLoading.set(true);
		this.searchService.getCadtTree().subscribe({
			next: (clubs) => {
				this.cadtTree.set(clubs);
				this.cadtTreeLoading.set(false);
			},
			error: () => {
				// Silent failure — CADT tree is optional (job may have no club reps)
				this.cadtTreeLoading.set(false);
			}
		});
	}

	executeSearch(): void {
		this.isSearching.set(true);
		const req = this.sanitizeRequest(this.searchRequest());
		this.lastSearchedRequest.set(JSON.stringify(req));
		this.searchService.search(req).subscribe({
			next: (results) => {
				this.searchResults.set(results);
				this.isSearching.set(false);
			},
			error: (err) => {
				this.toast.show('Search failed', 'danger', 4000);
				console.error('Search error:', err);
				this.isSearching.set(false);
			}
		});
	}

	clearFilters(): void {
		this.searchRequest.set({
			clubNames: [],
			levelOfPlays: [],
			agegroupIds: [],
			divisionIds: [],
			teamIds: [],
			activeStatuses: ['True'],
			payStatuses: [],
			cadtTeamIds: []
		});
		this.ladtCheckedIds.set(new Set());
		this.cadtCheckedIds.set(new Set());
		this.searchResults.set(null);
		this.lastSearchedRequest.set(null);
	}

	removeFilterChip(chip: FilterChip): void {
		// CADT tree chips: uncheck the node and re-derive
		if (chip.filterKey === 'cadtTeamIds') {
			const updated = new Set(this.cadtCheckedIds());
			this.removeCadtNode(updated, chip.value);
			this.onCadtCheckedChange(updated);
			this.executeSearch();
			return;
		}

		if (chip.filterKey === 'teamIds' || chip.filterKey === 'agegroupIds' || chip.filterKey === 'divisionIds') {
			const updated = new Set(this.ladtCheckedIds());
			updated.delete(chip.value);

			const removeDescendants = (nodes: LadtTreeNodeDto[]): boolean => {
				for (const n of nodes) {
					if (n.id === chip.value) {
						const collect = (items: LadtTreeNodeDto[]) => {
							for (const c of items) {
								updated.delete(c.id);
								collect((c.children ?? []) as LadtTreeNodeDto[]);
							}
						};
						collect((n.children ?? []) as LadtTreeNodeDto[]);
						return true;
					}
					const children = (n.children ?? []) as LadtTreeNodeDto[];
					if (children.length > 0 && removeDescendants(children)) return true;
				}
				return false;
			};
			removeDescendants(this.ladtTree());

			const findParent = (nodes: LadtTreeNodeDto[], targetId: string): string | null => {
				for (const n of nodes) {
					const children = (n.children ?? []) as LadtTreeNodeDto[];
					for (const c of children) {
						if (c.id === targetId) return n.id;
					}
					const found = findParent(children, targetId);
					if (found) return found;
				}
				return null;
			};
			let parentId = findParent(this.ladtTree(), chip.value);
			while (parentId) {
				updated.delete(parentId);
				parentId = findParent(this.ladtTree(), parentId);
			}

			this.onLadtCheckedChange(updated);
			this.executeSearch();
			return;
		}

		this.searchRequest.update(req => {
			const r = { ...req };
			const current = (r as any)[chip.filterKey];
			if (Array.isArray(current)) {
				(r as any)[chip.filterKey] = current.filter((v: any) => String(v) !== chip.value);
			}
			return r;
		});
		this.executeSearch();
	}

	clearAllChips(): void {
		this.clearFilters();
	}

	openDetail(teamId: string): void {
		this.searchService.getTeamDetail(teamId).subscribe({
			next: (detail) => {
				this.selectedDetail.set(detail);
				this.isPanelOpen.set(true);
			},
			error: (err) => {
				this.toast.show('Failed to load team detail', 'danger', 4000);
				console.error('Error loading detail:', err);
			}
		});
	}

	closePanel(): void {
		this.isPanelOpen.set(false);
		this.selectedDetail.set(null);
	}

	onDetailChanged(): void {
		this.executeSearch();
		const currentDetail = this.selectedDetail();
		if (currentDetail && this.isPanelOpen()) {
			this.openDetail(currentDetail.teamId);
		}
	}

	exportExcel(): void {
		const results = this.searchResults();
		if (this.grid && results) {
			this.grid.excelExport({ dataSource: results.result });
		}
	}

	onLadtCheckedChange(checkedIds: Set<string>): void {
		this.ladtCheckedIds.set(checkedIds);
		const teamIds: string[] = [];
		const agegroupIds: string[] = [];
		const divisionIds: string[] = [];

		const classify = (nodes: LadtTreeNodeDto[]) => {
			for (const node of nodes) {
				if (checkedIds.has(node.id)) {
					if (node.level === 1) agegroupIds.push(node.id);
					else if (node.level === 2) divisionIds.push(node.id);
					else if (node.level === 3) teamIds.push(node.id);
				}
				const children = (node.children ?? []) as LadtTreeNodeDto[];
				if (children.length > 0) classify(children);
			}
		};
		classify(this.ladtTree());

		this.searchRequest.update(req => ({ ...req, teamIds, agegroupIds, divisionIds }));
	}

	onCadtCheckedChange(checkedIds: Set<string>): void {
		this.cadtCheckedIds.set(checkedIds);

		// Resolve all CADT selections down to team IDs
		const cadtTeamIds: string[] = [];
		for (const id of checkedIds) {
			if (id.startsWith('team:')) {
				cadtTeamIds.push(id.replace('team:', ''));
			}
		}

		this.searchRequest.update(req => ({
			...req,
			cadtTeamIds
		}));
	}

	/** Remove a CADT node + its descendants + uncheck ancestors */
	private removeCadtNode(checked: Set<string>, nodeId: string): void {
		checked.delete(nodeId);

		const removeDescendants = (clubs: CadtClubNode[]) => {
			for (const club of clubs) {
				const clubId = `club:${club.clubName}`;
				if (clubId === nodeId) {
					for (const ag of club.agegroups ?? []) {
						checked.delete(`ag:${club.clubName}|${ag.agegroupId}`);
						for (const div of ag.divisions ?? []) {
							checked.delete(`div:${club.clubName}|${div.divId}`);
							for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
						}
					}
					return;
				}
				for (const ag of club.agegroups ?? []) {
					const agId = `ag:${club.clubName}|${ag.agegroupId}`;
					if (agId === nodeId) {
						for (const div of ag.divisions ?? []) {
							checked.delete(`div:${club.clubName}|${div.divId}`);
							for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
						}
						checked.delete(clubId);
						return;
					}
					for (const div of ag.divisions ?? []) {
						const divId = `div:${club.clubName}|${div.divId}`;
						if (divId === nodeId) {
							for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
							checked.delete(agId);
							checked.delete(clubId);
							return;
						}
						for (const team of div.teams ?? []) {
							if (`team:${team.teamId}` === nodeId) {
								checked.delete(divId);
								checked.delete(agId);
								checked.delete(clubId);
								return;
							}
						}
					}
				}
			}
		};
		removeDescendants(this.cadtTree());
	}

	updateMultiSelect(field: keyof TeamSearchRequest, values: string[]): void {
		this.searchRequest.update(req => ({ ...req, [field]: values ?? [] }));
	}

	private sanitizeRequest(req: TeamSearchRequest): TeamSearchRequest {
		const clean = (arr: any[] | null | undefined) => arr?.length ? arr : undefined;
		return {
			...req,
			clubNames: clean(req.clubNames),
			levelOfPlays: clean(req.levelOfPlays),
			agegroupIds: clean(req.agegroupIds),
			divisionIds: clean(req.divisionIds),
			teamIds: clean(req.teamIds),
			activeStatuses: clean(req.activeStatuses),
			payStatuses: clean(req.payStatuses),
			cadtTeamIds: clean(req.cadtTeamIds)
		};
	}
}
