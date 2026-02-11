import { Component, OnInit, OnDestroy, ViewChild, signal, computed, inject, ChangeDetectionStrategy, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent, PageSettingsModel, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { QueryCellInfoEventArgs } from '@syncfusion/ej2-grids';
import { MultiSelectModule, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';

import { RegistrationSearchService } from './services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { RegistrationDetailPanelComponent } from './components/registration-detail-panel.component';
import { RefundModalComponent } from './components/refund-modal.component';
import { BatchEmailModalComponent } from './components/batch-email-modal.component';
import { MobileQuickLookupComponent } from './components/mobile-quick-lookup.component';
import { LadtTreeFilterComponent } from './components/ladt-tree-filter.component';

import type {
  RegistrationSearchRequest,
  RegistrationSearchResponse,
  RegistrationFilterOptionsDto,
  RegistrationSearchResultDto,
  RegistrationDetailDto,
  AccountingRecordDto,
  FilterOption,
  LadtTreeNodeDto
} from '@core/api';

interface FilterChip {
  category: string;
  label: string;
  filterKey: keyof RegistrationSearchRequest;
  value: string;
}

@Component({
  selector: 'app-registration-search',
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
    LadtTreeFilterComponent
  ],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [CheckBoxSelectionService],
  templateUrl: './registration-search.component.html',
  styleUrl: './registration-search.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationSearchComponent implements OnInit, OnDestroy {
  private readonly searchService = inject(RegistrationSearchService);
  private readonly toast = inject(ToastService);

  @ViewChild('grid') grid!: GridComponent;

  // Filter options
  filterOptions = signal<RegistrationFilterOptionsDto | null>(null);

  // LADT tree state
  ladtTree = signal<LadtTreeNodeDto[]>([]);
  ladtCheckedIds = signal<Set<string>>(new Set());

  // Search state — multi-select arrays
  searchRequest = signal<RegistrationSearchRequest>({
    name: '',
    email: '',
    phone: '',
    schoolName: '',
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
    mobileRegistrationRoles: [],
    regDateFrom: undefined,
    regDateTo: undefined,
    rosterThreshold: undefined,
    rosterThresholdClub: undefined
  });

  searchResults = signal<RegistrationSearchResponse | null>(null);
  isSearching = signal(false);

  // Dirty-state tracking: detect when filters changed since last search
  private lastSearchedRequest = signal<string | null>(null);
  filtersAreDirty = computed(() => {
    const last = this.lastSearchedRequest();
    if (last === null) return false; // No search yet — not "dirty"
    return JSON.stringify(this.sanitizeRequest(this.searchRequest())) !== last;
  });

  // Expandable "More Filters" state
  moreFiltersExpanded = signal(false);

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
  pageSettings: PageSettingsModel = { pageSize: 20 };
  sortSettings: SortSettingsModel = { columns: [{ field: 'lastName', direction: 'Ascending' }] };

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
    addArrayChips('Mobile Reg', 'mobileRegistrationRoles', req.mobileRegistrationRoles, opts?.mobileRegistrations);

    if (req.name) chips.push({ category: 'Name', label: req.name, filterKey: 'name', value: req.name });
    if (req.email) chips.push({ category: 'Email', label: req.email, filterKey: 'email', value: req.email });
    if (req.phone) chips.push({ category: 'Phone', label: req.phone, filterKey: 'phone', value: req.phone });
    if (req.schoolName) chips.push({ category: 'School', label: req.schoolName, filterKey: 'schoolName', value: req.schoolName });
    if (req.regDateFrom) chips.push({ category: 'From', label: req.regDateFrom, filterKey: 'regDateFrom', value: req.regDateFrom });
    if (req.regDateTo) chips.push({ category: 'To', label: req.regDateTo, filterKey: 'regDateTo', value: req.regDateTo });
    if (req.rosterThreshold != null) chips.push({ category: 'Roster <=', label: String(req.rosterThreshold), filterKey: 'rosterThreshold', value: String(req.rosterThreshold) });
    if (req.rosterThresholdClub) chips.push({ category: 'Roster Club', label: req.rosterThresholdClub, filterKey: 'rosterThresholdClub', value: req.rosterThresholdClub });

    return chips;
  });

  ngOnInit(): void {
    this.checkMobileView();
    window.addEventListener('resize', this.resizeHandler);
    this.loadFilterOptions();
    this.loadLadtTree();
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
        // Store default state as baseline so any filter change triggers dirty immediately
        this.lastSearchedRequest.set(JSON.stringify(this.sanitizeRequest(this.searchRequest())));
      },
      error: (err) => {
        this.toast.show('Failed to load filter options', 'danger', 4000);
        console.error('Error loading filter options:', err);
      }
    });
  }

  private loadLadtTree(): void {
    this.searchService.getLadtTree().subscribe({
      next: (root) => this.ladtTree.set(root.leagues as LadtTreeNodeDto[]),
      error: (err) => console.error('Error loading LADT tree:', err)
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

  executeSearch(): void {
    this.isSearching.set(true);
    const req = this.sanitizeRequest(this.searchRequest());
    this.lastSearchedRequest.set(JSON.stringify(req));
    this.searchService.search(req).subscribe({
      next: (results) => {
        this.searchResults.set(results);
        this.selectedRegistrations.set(new Set());
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
      rosterThresholdClub: undefined
    });
    this.ladtCheckedIds.set(new Set());
    this.searchResults.set(null);
    this.lastSearchedRequest.set(null);
  }

  toggleMoreFilters(): void {
    this.moreFiltersExpanded.update(v => !v);
  }

  removeFilterChip(chip: FilterChip): void {
    // Tree-sourced chips: remove node + all descendants + ancestors, then re-derive
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
      } else if (chip.filterKey === 'rosterThresholdClub') {
        updated.rosterThresholdClub = undefined;
      } else {
        (updated as any)[chip.filterKey] = chip.filterKey === 'name' || chip.filterKey === 'email'
          || chip.filterKey === 'phone' || chip.filterKey === 'schoolName' ? '' : undefined;
      }
      return updated;
    });
    this.executeSearch();
  }

  clearAllChips(): void {
    this.clearFilters();
  }

  onRowSelected(): void {
    const selectedRecords = this.grid.getSelectedRecords() as RegistrationSearchResultDto[];
    const newSelection = new Set(selectedRecords.map(r => r.registrationId));
    this.selectedRegistrations.set(newSelection);
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
    this.executeSearch();

    const currentDetail = this.selectedDetail();
    if (currentDetail && this.isPanelOpen()) {
      this.openDetail(currentDetail.registrationId);
    }
  }

  exportExcel(): void {
    const results = this.searchResults();
    if (this.grid && results) {
      this.grid.excelExport({ dataSource: results.result });
    }
  }

  queryCellInfo(args: QueryCellInfoEventArgs): void {
    if (args.column?.headerText === 'Row' && args.cell) {
      const page = (this.grid.pageSettings.currentPage as number) || 1;
      const pageSize = (this.grid.pageSettings.pageSize as number) || 20;
      const currentViewData = this.grid.getCurrentViewRecords();
      const rowIndex = currentViewData.indexOf(args.data as any);
      if (rowIndex >= 0) {
        args.cell.textContent = String((page - 1) * pageSize + rowIndex + 1);
      }
    } else if (args.column?.field === 'owedTotal') {
      const record = args.data as RegistrationSearchResultDto;
      if (args.cell) {
        if (record.owedTotal === 0) {
          args.cell.classList.add('owed-zero');
        } else if (record.owedTotal > 0) {
          args.cell.classList.add('owed-positive');
        }
      }
    }
  }

  get canEmailSelected(): boolean {
    return this.selectedRegistrations().size > 0;
  }

  get selectedRegistrationIds(): string[] {
    return Array.from(this.selectedRegistrations());
  }

  onEmailSelected(): void {
    if (this.canEmailSelected) {
      this.showBatchEmailModal.set(true);
    }
  }

  onBatchEmailComplete(): void {
    this.showBatchEmailModal.set(false);
    this.toast.show('Batch email sent successfully', 'success', 4000);
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

  // ── Roster threshold helpers ──

  updateRosterThreshold(value: string): void {
    const num = value === '' || value == null ? undefined : Number(value);
    this.searchRequest.update(req => ({ ...req, rosterThreshold: num }));
  }

  updateRosterThresholdClub(value: string): void {
    this.searchRequest.update(req => ({ ...req, rosterThresholdClub: value || undefined }));
  }

  // ── Multi-select update helpers ──

  updateMultiSelect(field: keyof RegistrationSearchRequest, values: string[]): void {
    this.searchRequest.update(req => ({ ...req, [field]: values ?? [] }));
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

  updateRegDateFrom(value: string): void {
    this.searchRequest.update(req => ({ ...req, regDateFrom: value || undefined }));
  }

  updateRegDateTo(value: string): void {
    this.searchRequest.update(req => ({ ...req, regDateTo: value || undefined }));
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
      mobileRegistrationRoles: clean(req.mobileRegistrationRoles),
      rosterThreshold: req.rosterThreshold ?? undefined,
      rosterThresholdClub: req.rosterThresholdClub || undefined
    };
  }
}
