import { Component, OnInit, OnDestroy, HostListener, signal, computed, inject, ChangeDetectionStrategy, CUSTOM_ELEMENTS_SCHEMA, viewChild, viewChildren } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent, PageSettingsModel, SortSettingsModel, SelectionSettingsModel, DataStateChangeEventArgs, RowSelectEventArgs, RowDeselectEventArgs } from '@syncfusion/ej2-angular-grids';

import { MultiSelectModule, MultiSelectComponent, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';

import { RegistrationSearchService } from './services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { ROLE_ID_PLAYER, ROLE_ID_CLUBREP, isPlayerRoleFilter, isClubRepRoleFilter, type JobFlagsForTemplates } from './email-templates';
import { RegistrationDetailPanelComponent } from './components/registration-detail-panel.component';
import { RefundModalComponent } from './components/refund-modal.component';
import { BatchEmailModalComponent, type InviteMode } from './components/batch-email-modal.component';
import { MobileQuickLookupComponent } from './components/mobile-quick-lookup.component';
import { LadtTreeFilterComponent } from './components/ladt-tree-filter.component';
import { CadtTreeFilterComponent } from '@shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import { LocalStorageService } from '@infrastructure/services/local-storage.service';

import type {
  RegistrationSearchRequest,
  RegistrationSearchResponse,
  RegistrationFilterOptionsDto,
  RegistrationSearchResultDto,
  RegistrationDetailDto,
  AccountingRecordDto,
  FilterOption,
  LadtTreeNodeDto,
  CadtClubNode,
  JobOptionDto
} from '@core/api';

interface FilterChip {
  category: string;
  label: string;
  filterKey: keyof RegistrationSearchRequest;
  value: string;
}


@Component({
  selector: 'app-search-registrations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    GridAllModule,
    MultiSelectModule,
    RegistrationDetailPanelComponent,
    RefundModalComponent,
    BatchEmailModalComponent,
    MobileQuickLookupComponent,
    LadtTreeFilterComponent,
    CadtTreeFilterComponent,
    ConfirmDialogComponent
  ],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [CheckBoxSelectionService],
  templateUrl: './search-registrations.component.html',
  styleUrl: './search-registrations.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationSearchComponent implements OnInit, OnDestroy {
  private readonly searchService = inject(RegistrationSearchService);
  private readonly toast = inject(ToastService);
  private readonly jobService = inject(JobService);
  private readonly jobPulseService = inject(JobPulseService);
  private readonly localStorage = inject(LocalStorageService);

  /** True when the current job is a tournament — hides self-reported club filter */
  isTournament = computed(() =>
    this.jobService.currentJob()?.jobTypeName?.toLowerCase().includes('tournament') ?? false
  );

  /** Job pulse — drives Vertical Insure section visibility + template availability */
  readonly jobPulse = this.jobPulseService.pulse;
  readonly showViSection = computed(() => {
    const p = this.jobPulse();
    return !!(p?.offerPlayerRegsaverInsurance || p?.offerTeamRegsaverInsurance);
  });

  /**
   * Flags consumed by the template availability evaluator. Unified from pulse
   * (VI flags) and JobMetadataResponse (adnArb) so templates don't need to
   * know which DTO owns which flag.
   */
  readonly jobFlags = computed<JobFlagsForTemplates>(() => {
    const p = this.jobPulse();
    const m = this.jobService.currentJob();
    return {
      offerPlayerRegsaverInsurance: !!p?.offerPlayerRegsaverInsurance,
      offerTeamRegsaverInsurance: !!p?.offerTeamRegsaverInsurance,
      adnArb: !!m?.adnArb,
      // Gated purely on the admin having configured a USLax validation window.
      // Sport is intentionally NOT checked — if a non-Lacrosse job later needs a
      // USLax-style membership check, setting this date opts it in.
      usLaxMembershipValidated: !!m?.usLaxNumberValidThroughDate
    };
  });

  readonly grid = viewChild.required<GridComponent>('grid');
  readonly ladtTreeRef = viewChild<LadtTreeFilterComponent>('ladtTreeRef');
  readonly cadtTreeRef = viewChild<CadtTreeFilterComponent>('cadtTreeRef');
  readonly multiSelects = viewChildren(MultiSelectComponent);

  // Filter options
  filterOptions = signal<RegistrationFilterOptionsDto | null>(null);

  // LADT tree state
  ladtTree = signal<LadtTreeNodeDto[]>([]);
  ladtTreeLoading = signal(true);
  ladtCheckedIds = signal<Set<string>>(new Set());

  // CADT tree state (Club → Agegroup → Division → Team via ClubRepRegistrationId)
  cadtTree = signal<CadtClubNode[]>([]);
  cadtTreeLoading = signal(true);
  cadtCheckedIds = signal<Set<string>>(new Set());
  hasCadtData = computed(() => this.cadtTree().length > 0);

  // Job name derived from LADT root node (level 0) — used as CADT root label
  jobName = computed(() => this.ladtTree()[0]?.name ?? '');

  // Search state — multi-select arrays
  searchRequest = signal<RegistrationSearchRequest>({
    name: '',
    email: '',
    phone: '',
    schoolName: '',
    invoiceNumber: '',
    roleIds: [],
    teamIds: [],
    agegroupIds: [],
    divisionIds: [],
    clubNames: [],
    genders: [],
    positions: [],
    gradYears: [],
    grades: [],
    ageRangeIds: [],
    activeStatuses: ['True'],  // Default: Active pre-checked
    payStatuses: [],
    arbSubscriptionStatuses: [],
    regDateFrom: undefined,
    regDateTo: undefined,
    rosterThreshold: undefined,
    rosterThresholdClubNames: [],
    cadtTeamIds: [],
    hasVIPlayerInsurance: undefined,
    hasVITeamInsurance: undefined,
    arbHealthStatus: undefined,
    usLaxMembershipStatus: undefined
  });

  searchResults = signal<RegistrationSearchResponse | null>(null);
  isSearching = signal(false);

  // ── ARB CC Expiring This Month lookup ──
  // Transient mode: true while the grid is showing results from the live Authorize.net
  // lookup (not from the normal filter pipeline). Display-only; not part of searchRequest.
  arbCardExpiringMode = signal(false);
  isArbCardExpiringLoading = signal(false);

  // Filters fly-in panel state
  isFiltersPanelOpen = signal(false);

  // "Set Filters" discovery hint — a one-time directional arrow at the chip-strip
  // toggle so a first-time user can't miss where filters live. Retires permanently
  // the moment the panel is opened even once (persisted); by then they know the door.
  // Shared key with Search Teams — the two screens use the identical pattern, so
  // learning one teaches both.
  showFilterHint = signal(!this.localStorage.get(LocalStorageKey.SearchFiltersDiscovered, false));

  /** Toggle the filters fly-in; the first open retires the discovery hint for good. */
  toggleFiltersPanel(): void {
    this.isFiltersPanelOpen.set(!this.isFiltersPanelOpen());
    if (this.showFilterHint()) {
      this.showFilterHint.set(false);
      this.localStorage.set(LocalStorageKey.SearchFiltersDiscovered, true);
    }
  }

  // "Forgot to click Search" guard — set true when the user tries to close the
  // drawer (X / backdrop / Esc) with filter edits that were never run. Mirrors the
  // ladt editor's unsaved-changes guard; the centered dialog stays reachable even
  // when the footer Search button has scrolled below the fold.
  pendingFilterClose = signal(false);

  // Snapshot of the request that was last sent to the server. Used to light up
  // the Search button when the user has edited filters but not yet re-queried.
  private lastExecutedRequest = signal<string>('');

  isFilterDirty = computed(() =>
    JSON.stringify(this.searchRequest()) !== this.lastExecutedRequest()
  );

  // Selection state
  selectedRegistrations = signal<Set<string>>(new Set());

  // Detail panel state
  selectedDetail = signal<RegistrationDetailDto | null>(null);
  isPanelOpen = signal(false);

  // Modal state
  showBatchEmailModal = signal(false);
  showRefundModal = signal(false);
  refundTarget = signal<AccountingRecordDto | null>(null);

  // Mobile detection
  isMobile = signal(false);
  private resizeHandler = () => this.checkMobileView();

  // Grid configuration
  // Server-side paging: the grid fetches ONE page at a time (dataStateChange → fetchRegistrations).
  // Capped at 1000/page — no "All" (10K rows would render as a DOM bomb without virtualization).
  pageSettings: PageSettingsModel = { pageSize: 100, pageSizes: [100, 500, 1000] };
  sortSettings: SortSettingsModel = { columns: [{ field: 'lastName', direction: 'Ascending' }] };
  // persistSelection + a registrationId primary key keep checkboxes ticked across page fetches;
  // the authoritative selection set is maintained in selectedRegistrations (rowSelected/rowDeselected).
  selectionSettings: SelectionSettingsModel = { checkboxOnly: true, persistSelection: true };

  // ── Server-side paging state (drives the request; the grid reports skip/take/sort via
  // dataStateChange). Kept as separate signals so searchRequest stays pure filter criteria —
  // which lets it double as the batch-email Criteria payload unchanged. ──
  private gridPage = signal(1);
  private gridPageSize = signal(100);
  private gridSort = signal<{ field: string; dir: 'asc' | 'desc' }>({ field: 'lastName', dir: 'asc' });
  // Dedupe guard: the grid fires an initial dataStateChange after the first manual search; skip a
  // fetch whose (filters + page + size + sort) key matches the one already loaded.
  private lastFetchKey = '';

  // Syncfusion MultiSelect fields
  msFields = { value: 'value', text: 'text' };

  // Filter chips computed from active filter selections
  activeFilterChips = computed<FilterChip[]>(() => {
    const req = this.searchRequest();
    const opts = this.filterOptions();
    const chips: FilterChip[] = [];

    const addArrayChips = (
      category: string,
      filterKey: keyof RegistrationSearchRequest,
      values: any[] | null | undefined,
      options?: FilterOption[]
    ) => {
      if (!values?.length) return;
      for (const val of values) {
        const label = options?.find(o => o.value === String(val))?.text ?? String(val);
        chips.push({ category, label, filterKey, value: String(val) });
      }
    };

    addArrayChips('Role', 'roleIds', req.roleIds, opts?.roles);
    addArrayChips('Status', 'activeStatuses', req.activeStatuses, opts?.activeStatuses);
    addArrayChips('Pay', 'payStatuses', req.payStatuses, opts?.payStatuses);
    // LADT tree chips — show only the highest-level checked ancestor to avoid chip overload.
    // E.g., checking agegroup "2027" shows one "Agegroup: 2027" chip instead of 50+ team/division chips.
    const treeCheckedIds = this.ladtCheckedIds();
    if (treeCheckedIds.size > 0) {
      const levelConfig: Record<number, { category: string; filterKey: keyof RegistrationSearchRequest }> = {
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
    addArrayChips('Club', 'clubNames', req.clubNames, opts?.clubs);
    addArrayChips('Gender', 'genders', req.genders, opts?.genders);
    addArrayChips('Position', 'positions', req.positions, opts?.positions);
    addArrayChips('Grad Year', 'gradYears', req.gradYears, opts?.gradYears);
    addArrayChips('Grade', 'grades', req.grades, opts?.grades);
    addArrayChips('Age Range', 'ageRangeIds', req.ageRangeIds, opts?.ageRanges);
    addArrayChips('Subscription', 'arbSubscriptionStatuses', req.arbSubscriptionStatuses, opts?.arbSubscriptionStatuses);
    addArrayChips('Payment Type', 'paymentTypes', req.paymentTypes, opts?.paymentTypes);
    if (req.name) chips.push({ category: 'Name', label: req.name, filterKey: 'name', value: req.name });
    if (req.email) chips.push({ category: 'Email', label: req.email, filterKey: 'email', value: req.email });
    if (req.phone) chips.push({ category: 'Phone', label: req.phone, filterKey: 'phone', value: req.phone });
    if (req.schoolName) chips.push({ category: 'School', label: req.schoolName, filterKey: 'schoolName', value: req.schoolName });
    if (req.regDateFrom) chips.push({ category: 'On or After', label: req.regDateFrom, filterKey: 'regDateFrom', value: req.regDateFrom });
    if (req.rosterThreshold != null) chips.push({ category: 'Rostered <=', label: String(req.rosterThreshold), filterKey: 'rosterThreshold', value: String(req.rosterThreshold) });
    if (req.hasVIPlayerInsurance != null) {
      chips.push({
        category: 'Player Ins.',
        label: req.hasVIPlayerInsurance ? 'Has Insurance' : 'Not Yet Accepted',
        filterKey: 'hasVIPlayerInsurance',
        value: String(req.hasVIPlayerInsurance)
      });
    }
    if (req.hasVITeamInsurance != null) {
      chips.push({
        category: 'Team Ins.',
        label: req.hasVITeamInsurance ? 'All Teams Covered' : 'At Least One Uncovered',
        filterKey: 'hasVITeamInsurance',
        value: String(req.hasVITeamInsurance)
      });
    }
    if (req.arbHealthStatus) {
      chips.push({
        category: 'ARB Health',
        label: this.arbHealthLabel(req.arbHealthStatus),
        filterKey: 'arbHealthStatus',
        value: req.arbHealthStatus
      });
    }
    addArrayChips('For Club', 'rosterThresholdClubNames', req.rosterThresholdClubNames, opts?.clubRepClubs);

    // CADT tree chips — highest-level checked ancestor only (same pattern as LADT)
    const cadtChecked = this.cadtCheckedIds();
    if (cadtChecked.size > 0 && this.cadtTree().length > 0) {
      for (const club of this.cadtTree()) {
        const clubId = `club:${club.clubName}`;
        if (cadtChecked.has(clubId)) {
          chips.push({ category: 'Club Team', label: club.clubName, filterKey: 'cadtTeamIds', value: clubId });
          continue; // Skip children — club-level chip covers them
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

  ngOnInit(): void {
    this.checkMobileView();
    window.addEventListener('resize', this.resizeHandler);
    this.loadFilterOptions();
    this.loadLadtTree();
    this.loadCadtTree();
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.resizeHandler);
  }

  private checkMobileView(): void {
    this.isMobile.set(window.matchMedia('(max-width: 767px)').matches);
  }

  loadFilterOptions(): void {
    this.searchService.getFilterOptions().subscribe({
      next: (options) => {
        this.filterOptions.set(options);
        this.applyDefaultChecked(options);
        // Auto-search on load with default filters
        this.executeSearch();
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
    this.searchService.getCadtTree(skipErrorToast()).subscribe({
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

  /** Apply DefaultChecked values from filter options to the search request */
  private applyDefaultChecked(opts: RegistrationFilterOptionsDto): void {
    const getDefaults = (options: FilterOption[] | null | undefined) =>
      options?.filter(o => o.defaultChecked).map(o => o.value) ?? [];

    const defaultGenders = getDefaults(opts.genders);

    this.searchRequest.update(req => ({
      ...req,
      genders: defaultGenders.length ? defaultGenders : req.genders
    }));
  }

  /** A new search from the filter panel / Search button: reset to page 1, clear selection, force a fetch. */
  executeSearch(keepPanelOpen = false): void {
    if (this.isSearching()) return; // Guard against double-fire (Enter bubbling + button click)
    this.arbCardExpiringMode.set(false);
    if (!keepPanelOpen) this.isFiltersPanelOpen.set(false);
    this.gridPage.set(1);
    this.lastExecutedRequest.set(JSON.stringify(this.searchRequest()));
    // Fetch FIRST so lastFetchKey registers page-1 for the new criteria, THEN reset the pager UI:
    // goToPage(1)'s dataStateChange re-asks for the same page-1 key and dedupes (no double request).
    this.fetchRegistrations({ clearSelection: true, force: true });
    // Reset the pager only if the grid is already rendered. grid() is a REQUIRED query that THROWS
    // (NG0951) when absent — and the grid lives under @if(searchResults()), so on the first/auto
    // search there's no grid yet (it renders at page 1 by default). searchResults() still holds the
    // pre-fetch value here, so it correctly reflects whether the grid is currently in the DOM.
    if (this.searchResults()) this.grid().goToPage(1);
  }

  /**
   * The grid asks for a page/sort (init, pager click, column sort). Update paging state and fetch.
   * Selection is preserved across paging/sorting — that's the whole point of server-side selection.
   */
  onDataStateChange(state: DataStateChangeEventArgs): void {
    // ARB card-expiring results come from a live lookup (a fixed, unpaged id set), not the filter
    // pipeline — never let a grid-driven page/sort re-run the normal search and clobber them.
    if (this.arbCardExpiringMode()) return;
    const take = state.take && state.take > 0 ? state.take : this.gridPageSize();
    const skip = state.skip ?? 0;
    this.gridPageSize.set(take);
    this.gridPage.set(Math.floor(skip / take) + 1);
    const sorted = state.sorted?.[0];
    this.gridSort.set(sorted
      ? { field: String(sorted.name), dir: sorted.direction === 'descending' ? 'desc' : 'asc' }
      : { field: 'lastName', dir: 'asc' });
    this.fetchRegistrations({ clearSelection: false, force: false });
  }

  /** Single fetch chokepoint. Builds filters + paging + sort, dedupes, and sets searchResults. */
  private fetchRegistrations(opts: { clearSelection: boolean; force: boolean }): void {
    const sort = this.gridSort();
    const req: RegistrationSearchRequest = {
      ...this.sanitizeRequest(this.searchRequest()),
      page: this.gridPage(),
      pageSize: this.gridPageSize(),
      sortField: sort.field,
      sortDir: sort.dir
    };
    const key = JSON.stringify(req);
    if (!opts.force && key === this.lastFetchKey) return; // grid re-asked for what we already have
    this.lastFetchKey = key;
    this.isSearching.set(true);
    if (opts.clearSelection) this.selectedRegistrations.set(new Set());
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

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    // While the discard prompt is up, let the dialog own Escape (it dismisses).
    if (this.pendingFilterClose()) return;
    if (this.isFiltersPanelOpen()) {
      this.requestCloseFilters();
    }
  }

  /** User-initiated drawer close (X / backdrop / Esc). If filters were edited but
   *  never searched, surface the centered "forgot to search" prompt instead of
   *  silently discarding the un-applied edits. */
  requestCloseFilters(): void {
    if (this.isFilterDirty()) {
      this.pendingFilterClose.set(true);
      return;
    }
    this.isFiltersPanelOpen.set(false);
  }

  /** "Search now" — run the staged filters (executeSearch closes the panel + records
   *  lastExecutedRequest, which clears the dirty state). */
  onFilterCloseSearch(): void {
    this.pendingFilterClose.set(false);
    this.executeSearch();
  }

  /** "Close without searching" — close the drawer but keep the staged filters; they
   *  persist as chips and the chip-strip Search button keeps pulsing. Also the path
   *  for dismissing the dialog itself (its X / backdrop / Esc). */
  onFilterCloseDiscard(): void {
    this.pendingFilterClose.set(false);
    this.isFiltersPanelOpen.set(false);
  }


  clearFilters(): void {
    const opts = this.filterOptions();
    const defaultGenders = opts?.genders?.filter(o => o.defaultChecked).map(o => o.value) ?? [];

    this.searchRequest.set({
      name: '',
      email: '',
      phone: '',
      schoolName: '',
      roleIds: [],
      teamIds: [],
      agegroupIds: [],
      divisionIds: [],
      clubNames: [],
      genders: defaultGenders,
      positions: [],
      gradYears: [],
      grades: [],
      ageRangeIds: [],
      activeStatuses: ['True'],
      payStatuses: [],
      arbSubscriptionStatuses: [],
      mobileRegistrationRoles: [],
      regDateFrom: undefined,
      regDateTo: undefined,
      rosterThreshold: undefined,
      rosterThresholdClubNames: [],
      cadtTeamIds: [],
      hasVIPlayerInsurance: undefined,
      hasVITeamInsurance: undefined,
      arbHealthStatus: undefined,
      usLaxMembershipStatus: undefined
    });
    this.ladtCheckedIds.set(new Set());
    this.cadtCheckedIds.set(new Set());
    this.treesCollapsed.set(false);
    // Reset runs the (now-default) search but leaves the panel as-is, so the
    // drawer stays open after Reset instead of snapping shut. From the chip-strip
    // the panel is already closed, so this preserves that too.
    this.executeSearch(true);
  }

  /** Trees collapsed state — collapses when a multiselect opens */
  treesCollapsed = signal(false);

  /** Close all other multiselect popups and collapse trees when one opens */
  onMultiSelectOpen(opened: MultiSelectComponent): void {
    this.multiSelects()?.forEach(ms => {
      if (ms !== opened) ms.hidePopup();
    });
    this.treesCollapsed.set(true);
  }

  /** Expand trees and close all multiselect popups */
  expandTrees(): void {
    this.treesCollapsed.set(false);
    this.closeAllMultiSelects();
  }

  /** Close every multiselect popup */
  closeAllMultiSelects(): void {
    this.multiSelects()?.forEach(ms => ms.hidePopup());
  }

  removeFilterChip(chip: FilterChip): void {
    // Scoped-filter chips (VI / ARB) must go through their dedicated updaters
    // so the auto-enacted side-effects (role auto-clear, etc.) stay consistent.
    if (chip.filterKey === 'hasVIPlayerInsurance') {
      this.applyViFilter('hasVIPlayerInsurance', undefined, ROLE_ID_PLAYER);
      this.executeSearch();
      return;
    }
    if (chip.filterKey === 'hasVITeamInsurance') {
      this.applyViFilter('hasVITeamInsurance', undefined, ROLE_ID_CLUBREP);
      this.executeSearch();
      return;
    }
    if (chip.filterKey === 'arbHealthStatus') {
      this.updateArbHealthFilter('');
      this.executeSearch();
      return;
    }

    // CADT tree chips: uncheck the node and re-derive
    if (chip.filterKey === 'cadtTeamIds') {
      const updated = new Set(this.cadtCheckedIds());
      // Find the flat node in the CADT tree and remove it + descendants + ancestors
      this.removeCadtNode(updated, chip.value);
      this.onCadtCheckedChange(updated);
      this.executeSearch();
      return;
    }

    // LADT tree-sourced chips: remove node + all descendants + ancestors, then re-derive
    if (chip.filterKey === 'teamIds' || chip.filterKey === 'agegroupIds' || chip.filterKey === 'divisionIds') {
      const updated = new Set(this.ladtCheckedIds());
      updated.delete(chip.value);

      // Remove all descendants of the target node
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

      // Remove ancestors (they can't be fully checked anymore)
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

      // Re-derive searchRequest from updated checked set
      this.onLadtCheckedChange(updated);
      this.executeSearch();
      return;
    }

    this.searchRequest.update(req => {
      const updated = { ...req };
      const current = (updated as any)[chip.filterKey];
      if (Array.isArray(current)) {
        (updated as any)[chip.filterKey] = current.filter((v: any) => String(v) !== chip.value);
      } else if (chip.filterKey === 'rosterThreshold') {
        updated.rosterThreshold = undefined;
      } else {
        (updated as any)[chip.filterKey] = chip.filterKey === 'name' || chip.filterKey === 'email'
          || chip.filterKey === 'phone' || chip.filterKey === 'schoolName' ? '' : undefined;
      }
      return updated;
    });
    this.executeSearch();
  }

  // Cross-page selection: the authoritative recipient set is selectedRegistrations, accumulated by
  // registrationId. We ADD/REMOVE per (de)selection delta rather than snapshotting the grid's current
  // view — with server-side paging getSelectedRecords() only ever sees the loaded page, so overwriting
  // from it would silently drop off-page picks. Header select-all here = "select the current page"
  // (Syncfusion can't select rows it hasn't fetched); Email All is its own explicit button.
  private isRestoringSelection = false;

  onRowSelected(args: RowSelectEventArgs): void {
    this.applySelectionDelta(args?.data, true);
  }

  onRowDeselected(args: RowDeselectEventArgs): void {
    this.applySelectionDelta(args?.data, false);
  }

  private applySelectionDelta(data: unknown, add: boolean): void {
    if (this.isRestoringSelection) return;
    const rows = (Array.isArray(data) ? data : data ? [data] : []) as RegistrationSearchResultDto[];
    if (rows.length === 0) return;
    const next = new Set(this.selectedRegistrations());
    for (const r of rows) {
      if (!r?.registrationId) continue;
      if (add) next.add(r.registrationId);
      else next.delete(r.registrationId);
    }
    this.selectedRegistrations.set(next);
    if (next.size > 0) this.emailMode.set('selected');
  }

  // Fires after every page render (initial load + each server-side page fetch). Re-tick persisted
  // selections first, then stamp the full-set row numbers over the (possibly re-rendered) rows.
  onGridDataBound(): void {
    this.restorePageSelection();
    this.refreshRowNumbers();
  }

  // Re-check the checkboxes for rows whose id is already in the selection set, so paging back to an
  // earlier page shows the picks intact. Guarded so the programmatic reselect doesn't churn the set
  // through onRowSelected.
  private restorePageSelection(): void {
    const set = this.selectedRegistrations();
    if (set.size === 0) return;
    const grid = this.grid();
    if (!grid) return;
    const view = grid.getCurrentViewRecords() as RegistrationSearchResultDto[];
    const indexes: number[] = [];
    view.forEach((r, i) => { if (set.has(r.registrationId)) indexes.push(i); });
    if (indexes.length === 0) return;
    this.isRestoringSelection = true;
    try { grid.selectRows(indexes); }
    finally { this.isRestoringSelection = false; }
  }

  onActionComplete(args: any): void {
    if (args.requestType === 'sorting' || args.requestType === 'paging') {
      this.refreshRowNumbers();
    }
  }

  refreshRowNumbers(): void {
    // Row numbers are 1-based across the full set — offset by the current server page, not the
    // grid's internal pager state (which we drive via dataStateChange).
    const pageSize = this.gridPageSize();
    const currentPage = this.gridPage();
    const start = (currentPage - 1) * pageSize;
    const gridEl = this.grid().element;
    if (!gridEl) return;
    const rows = gridEl.querySelectorAll('.e-frozencontent tbody tr, .e-frozencontentdiv tbody tr');
    if (rows.length) {
      rows.forEach((row, i) => {
        const cell = row.querySelector('td.e-rowcell');
        if (cell) cell.textContent = String(start + i + 1);
      });
    } else {
      this.grid().getRows().forEach((row, i) => {
        const cell = row.querySelector('td.e-rowcell');
        if (cell) cell.textContent = String(start + i + 1);
      });
    }
  }

  openDetail(registrationId: string): void {
    this.searchService.getRegistrationDetail(registrationId).subscribe({
      next: (detail) => {
        this.selectedDetail.set(detail);
        this.isPanelOpen.set(true);
      },
      error: (err) => {
        this.toast.show('Failed to load registration detail', 'danger', 4000);
        console.error('Error loading detail:', err);
      }
    });
  }

  closePanel(): void {
    this.isPanelOpen.set(false);
    this.selectedDetail.set(null);
  }

  onRefundRequested(record: AccountingRecordDto): void {
    this.refundTarget.set(record);
    this.showRefundModal.set(true);
  }

  onRefundComplete(): void {
    this.showRefundModal.set(false);
    this.refundTarget.set(null);
    this.toast.show('Refund processed successfully', 'success', 4000);
    this.refreshAfterChange();
  }

  onDetailSaved(): void {
    this.refreshAfterChange();
  }

  private refreshAfterChange(): void {
    this.executeSearch();
    const currentDetail = this.selectedDetail();
    if (currentDetail && this.isPanelOpen()) {
      this.openDetail(currentDetail.registrationId);
    }
  }

  exportExcel(): void {
    const grid = this.grid();
    if (!grid) return;

    // ARB card-expiring results are already the full unpaged set — export what's loaded.
    if (this.arbCardExpiringMode()) {
      const loaded = this.searchResults()?.result ?? [];
      grid.excelExport({ dataSource: loaded, includeHiddenColumn: true });
      return;
    }

    // The grid holds only ONE page; export must be the whole match. Re-run the search with no paging
    // params (backend returns all) and export that. Explicit user action, so a heavy 10K pull is fine.
    const req: RegistrationSearchRequest = { ...this.sanitizeRequest(this.searchRequest()) };
    this.isSearching.set(true);
    this.searchService.search(req).subscribe({
      next: (full) => {
        this.isSearching.set(false);
        grid.excelExport({ dataSource: full.result, includeHiddenColumn: true });
      },
      error: (err) => {
        this.isSearching.set(false);
        this.toast.show('Export failed', 'danger', 4000);
        console.error('Export error:', err);
      }
    });
  }


  // Email mode: 'selected' = checked rows, 'all' = full query results
  emailMode = signal<'selected' | 'all'>('selected');

  get canEmailSelected(): boolean {
    return this.selectedRegistrations().size > 0;
  }

  // 'all' mode normally sends NO ids — the server re-resolves the full audience from the search
  // criteria at send time (correct even though only one page is loaded client-side). EXCEPTION: an
  // ARB card-expiring lookup isn't reproducible from filter criteria, so its 'all' sends the loaded
  // (fully unpaged) id set explicitly. 'selected' sends the accumulated id set — which may include
  // off-page picks the grid no longer holds.
  get emailRegistrationIds(): string[] {
    if (this.emailMode() === 'all') {
      return this.arbCardExpiringMode()
        ? (this.searchResults()?.result?.map(r => r.registrationId) ?? [])
        : [];
    }
    return Array.from(this.selectedRegistrations());
  }

  // The exact filter criteria that produced the on-screen results, sanitized so an "Email All" send
  // re-resolves the IDENTICAL audience server-side (empty arrays → undefined, matching the search
  // request that ran). Also feeds the modal's template-availability gating.
  readonly emailCriteria = computed(() => this.sanitizeRequest(this.searchRequest()));

  // Authoritative recipient count: 'all' = the full-set server count (NOT the loaded page length),
  // 'selected' = the accumulated set size.
  get emailRecipientCount(): number {
    if (this.emailMode() === 'all') {
      return this.searchResults()?.count ?? 0;
    }
    return this.selectedRegistrations().size;
  }

  // Display-only name sample. Off-page recipients aren't in memory, so this is best-effort from the
  // loaded page; emailRecipientCount is the authoritative headline and the send is resolved server-side.
  get emailRecipients(): { name: string; email: string }[] {
    const loaded = this.searchResults()?.result ?? [];
    const rows = this.emailMode() === 'all'
      ? loaded
      : loaded.filter(r => this.selectedRegistrations().has(r.registrationId));
    return rows.map(r => ({
      name: `${r.lastName}, ${r.firstName}`,
      email: r.email
    }));
  }

  onEmailSelected(): void {
    if (this.canEmailSelected) {
      this.inviteMode.set(null);
      this.emailMode.set('selected');
      this.showBatchEmailModal.set(true);
    }
  }

  onEmailAll(): void {
    if (this.searchResults()?.count) {
      this.grid().clearSelection();
      this.selectedRegistrations.set(new Set());
      this.inviteMode.set(null);
      this.emailMode.set('all');
      this.showBatchEmailModal.set(true);
    }
  }

  // ── Invite action (first-class, decoupled from hand-composing email) ──
  // A signed-invite send is offered when the current search is scoped to exactly ONE invitable role
  // AND the job-scoped init load found eligible target events for it. Eligibility (same customer +
  // job type, not expired, role's reg flag on) is computed server-side; the arrays ride on the
  // filter-options DTO so this is a pure read, no per-open fetch.
  inviteMode = signal<InviteMode | null>(null);

  private readonly singleRoleFilter = computed(() => {
    const ids = this.searchRequest().roleIds ?? [];
    return ids.length === 1 ? ids[0] : null;
  });

  readonly eligiblePlayerInviteTargetJobs = computed<JobOptionDto[]>(
    () => this.filterOptions()?.eligiblePlayerInviteTargetJobs ?? []);
  readonly eligibleClubRepInviteTargetJobs = computed<JobOptionDto[]>(
    () => this.filterOptions()?.eligibleClubRepInviteTargetJobs ?? []);

  /** The invite role the current single-role search qualifies for, or null when it doesn't
   *  (multiple/zero roles, or no eligible target events for that role). Drives the Invite button. */
  readonly invitableRole = computed<InviteMode | null>(() => {
    const role = this.singleRoleFilter();
    if (!role) return null;
    if (isPlayerRoleFilter(role) && this.eligiblePlayerInviteTargetJobs().length > 0) return 'player';
    if (isClubRepRoleFilter(role) && this.eligibleClubRepInviteTargetJobs().length > 0) return 'clubrep';
    return null;
  });

  /** Eligible target events for the active invitable role — passed straight to the modal. */
  readonly activeInviteTargetJobs = computed<JobOptionDto[]>(() => {
    switch (this.invitableRole()) {
      case 'player': return this.eligiblePlayerInviteTargetJobs();
      case 'clubrep': return this.eligibleClubRepInviteTargetJobs();
      default: return [];
    }
  });

  /** Role-aware button label that flips on selection: "Invite all Players" ↔ "Invite 3 checked Players". */
  readonly inviteButtonLabel = computed(() => {
    const mode = this.invitableRole();
    if (!mode) return '';
    const noun = mode === 'player' ? 'Players' : 'Club Reps';
    const checked = this.selectedRegistrations().size;
    return checked > 0 ? `Invite ${checked} checked ${noun}` : `Invite all ${noun}`;
  });

  onInvite(): void {
    const mode = this.invitableRole();
    if (!mode) return;
    // Neutral selection: checked subset → those recipients; nothing checked → the full result set.
    this.emailMode.set(this.selectedRegistrations().size > 0 ? 'selected' : 'all');
    this.inviteMode.set(mode);
    this.showBatchEmailModal.set(true);
  }

  onBatchEmailComplete(): void {
    // Leave the modal open: it renders the completion summary + "Email me this summary"
    // and shows its own result toast. The user closes it when done reviewing.
  }

  toggleEmailOptOut(data: RegistrationSearchResultDto): void {
    const newValue = !data.emailOptOut;
    this.searchService.setEmailOptOut(data.registrationId, newValue).subscribe({
      next: () => {
        (data as Record<string, unknown>)['emailOptOut'] = newValue;
        this.grid().refresh();
        this.toast.show(newValue ? 'Opted out of emails' : 'Opted back in to emails', 'success', 3000);
      },
      error: () => this.toast.show('Failed to update opt-out status', 'danger', 4000)
    });
  }

  // ── LADT tree selection handler ──

  onLadtCheckedChange(checkedIds: Set<string>): void {
    this.ladtCheckedIds.set(checkedIds);

    // Derive teamIds/agegroupIds/divisionIds from checked tree nodes
    const teamIds: string[] = [];
    const agegroupIds: string[] = [];
    const divisionIds: string[] = [];

    // Classify ALL checked nodes by level. Always recurse so the backend OR filter
    // catches registrations assigned at any level (team, division, or agegroup).
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

    this.searchRequest.update(req => ({
      ...req,
      teamIds,
      agegroupIds,
      divisionIds
    }));
  }

  // ── CADT tree selection handler ──

  onCadtCheckedChange(checkedIds: Set<string>): void {
    this.cadtCheckedIds.set(checkedIds);

    // Resolve all CADT selections down to team IDs (same pattern as scheduling)
    const cadtTeamIds: string[] = [];
    for (const id of checkedIds) {
      if (id.startsWith('team:')) {
        cadtTeamIds.push(id.replace('team:', ''));
      }
    }

    this.searchRequest.update(req => ({
      ...req,
      cadtTeamIds: cadtTeamIds
    }));
  }

  /** Remove a CADT node + its descendants + uncheck ancestors */
  private removeCadtNode(checked: Set<string>, nodeId: string): void {
    checked.delete(nodeId);

    // Walk the CADT tree to find descendants and remove them
    const removeDescendants = (clubs: CadtClubNode[]) => {
      for (const club of clubs) {
        const clubId = `club:${club.clubName}`;
        if (clubId === nodeId) {
          // Remove all descendants of this club
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
            // Uncheck club ancestor
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

  // ── Roster threshold helpers ──

  updateRosterThreshold(value: string): void {
    const num = value === '' || value == null ? undefined : Number(value);
    this.searchRequest.update(req => ({ ...req, rosterThreshold: num }));
  }

  // ── Vertical Insure filters ──

  readonly viPlayerFilterValue = computed(() => {
    const v = this.searchRequest().hasVIPlayerInsurance;
    return v == null ? '' : String(v);
  });

  readonly viTeamFilterValue = computed(() => {
    const v = this.searchRequest().hasVITeamInsurance;
    return v == null ? '' : String(v);
  });

  updateViPlayerFilter(value: string): void {
    const parsed = value === '' ? undefined : value === 'true';
    this.applyViFilter('hasVIPlayerInsurance', parsed, ROLE_ID_PLAYER);
  }

  updateViTeamFilter(value: string): void {
    const parsed = value === '' ? undefined : value === 'true';
    this.applyViFilter('hasVITeamInsurance', parsed, ROLE_ID_CLUBREP);
  }

  /**
   * Set (or clear) a VI filter and auto-enact its implied state.
   *   Enable : force roleIds=[impliedRole] and activeStatuses=['True'] (VI is active-only, role-scoped).
   *   Disable: clear roleIds IFF it still equals exactly [impliedRole]. activeStatuses is left alone —
   *            ['True'] is the baseline default, so no-op on disable.
   */
  private applyViFilter(
    field: 'hasVIPlayerInsurance' | 'hasVITeamInsurance',
    value: boolean | undefined,
    impliedRoleId: string
  ): void {
    this.searchRequest.update(req => {
      const update: Partial<RegistrationSearchRequest> = { [field]: value };
      if (value !== undefined) {
        update.roleIds = [impliedRoleId];
        update.activeStatuses = ['True'];
      } else {
        const current = req.roleIds ?? [];
        if (current.length === 1 && current[0]?.toUpperCase() === impliedRoleId.toUpperCase()) {
          update.roleIds = [];
        }
      }
      return { ...req, ...update };
    });
  }

  // ── ARB Health filter ──

  readonly arbHealthFilterValue = computed(() => this.searchRequest().arbHealthStatus ?? '');

  readonly showArbSection = computed(() => this.jobService.currentJob()?.adnArb === true);

  updateArbHealthFilter(value: string): void {
    const parsed = value === '' ? undefined : value;
    this.searchRequest.update(req => {
      const update: Partial<RegistrationSearchRequest> = { arbHealthStatus: parsed };
      if (parsed !== undefined) {
        update.activeStatuses = ['True'];
      }
      return { ...req, ...update };
    });
  }

  private arbHealthLabel(value: string): string {
    switch (value) {
      case 'behind-active': return 'Behind — Active/Suspended';
      case 'behind-expired': return 'Behind — Expired/Terminated';
      default: return value;
    }
  }

  /** Live lookup against Authorize.net for subscriptions with cards expiring this month.
   *  Bypasses filter state — dropped/inactive registrants with expiring cards still
   *  need to surface so admins can follow up before the next auto-bill fails. */
  runArbCardExpiringLookup(): void {
    if (this.isArbCardExpiringLoading()) return;
    this.isArbCardExpiringLoading.set(true);
    this.isFiltersPanelOpen.set(false);
    this.searchService.arbCardExpiringLookup().subscribe({
      next: (results) => {
        // ARB results are the full unpaged set. Reset to page 1 so row numbering (offset by gridPage)
        // starts at 1; the dataStateChange guard keeps the pager from re-running the filter search.
        // Guard the pager reset — grid() is a required query that throws when not yet rendered.
        this.gridPage.set(1);
        if (this.searchResults()) this.grid().goToPage(1);
        this.searchResults.set(results);
        this.selectedRegistrations.set(new Set());
        this.arbCardExpiringMode.set(true);
        this.isArbCardExpiringLoading.set(false);
        if (results.count === 0) {
          this.toast.show('No cards expiring this month for this job', 'info', 4000);
        }
      },
      error: (err) => {
        this.isArbCardExpiringLoading.set(false);
        const msg = err?.error?.message ?? 'Authorize.net lookup failed';
        this.toast.show(msg, 'danger', 5000);
      }
    });
  }

  /** Exit lookup mode and restore the normal search using whatever filters are set. */
  clearArbCardExpiringMode(): void {
    this.arbCardExpiringMode.set(false);
    this.executeSearch();
  }

  // ── Multi-select update helpers ──

  updateMultiSelect(field: keyof RegistrationSearchRequest, values: string[]): void {
    this.searchRequest.update(req => {
      const updated = { ...req, [field]: values ?? [] };
      // Selecting a payment method implies searching across all active statuses
      if (field === 'paymentTypes' && values?.length) {
        updated.activeStatuses = [];
      }
      return updated;
    });
  }

  updateName(value: string): void {
    this.searchRequest.update(req => ({ ...req, name: value }));
  }

  updateEmail(value: string): void {
    this.searchRequest.update(req => ({ ...req, email: value }));
  }

  updatePhone(value: string): void {
    this.searchRequest.update(req => ({ ...req, phone: value }));
  }

  updateSchoolName(value: string): void {
    this.searchRequest.update(req => ({ ...req, schoolName: value }));
  }

  updateInvoiceNumber(value: string): void {
    this.searchRequest.update(req => ({ ...req, invoiceNumber: value }));
  }

  updateRegDateFrom(value: string): void {
    this.searchRequest.update(req => ({ ...req, regDateFrom: value || undefined }));
  }

  /** Convert empty arrays to undefined so backend ignores them */
  private sanitizeRequest(req: RegistrationSearchRequest): RegistrationSearchRequest {
    const clean = (arr: any[] | null | undefined) => arr?.length ? arr : undefined;
    return {
      ...req,
      name: req.name || undefined,
      email: req.email || undefined,
      phone: req.phone || undefined,
      schoolName: req.schoolName || undefined,
      roleIds: clean(req.roleIds),
      teamIds: clean(req.teamIds),
      agegroupIds: clean(req.agegroupIds),
      divisionIds: clean(req.divisionIds),
      clubNames: clean(req.clubNames),
      genders: clean(req.genders),
      positions: clean(req.positions),
      gradYears: clean(req.gradYears),
      grades: clean(req.grades),
      ageRangeIds: clean(req.ageRangeIds),
      activeStatuses: clean(req.activeStatuses),
      payStatuses: clean(req.payStatuses),
      arbSubscriptionStatuses: clean(req.arbSubscriptionStatuses),
      paymentTypes: clean(req.paymentTypes),
      rosterThreshold: req.rosterThreshold ?? undefined,
      rosterThresholdClubNames: clean(req.rosterThresholdClubNames),
      cadtTeamIds: clean(req.cadtTeamIds),
      arbHealthStatus: req.arbHealthStatus || undefined
    };
  }
}
