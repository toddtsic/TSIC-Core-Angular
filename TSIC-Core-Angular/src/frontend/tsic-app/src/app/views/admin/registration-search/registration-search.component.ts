import { Component, OnInit, OnDestroy, ViewChild, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent, PageSettingsModel, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { QueryCellInfoEventArgs } from '@syncfusion/ej2-grids';

import { RegistrationSearchService } from './services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { RegistrationDetailPanelComponent } from './components/registration-detail-panel.component';
import { RefundModalComponent } from './components/refund-modal.component';
import { BatchEmailModalComponent } from './components/batch-email-modal.component';
import { MobileQuickLookupComponent } from './components/mobile-quick-lookup.component';

import type {
  RegistrationSearchRequest,
  RegistrationSearchResponse,
  RegistrationFilterOptionsDto,
  RegistrationSearchResultDto,
  RegistrationDetailDto,
  AccountingRecordDto
} from '@core/api';

@Component({
  selector: 'app-registration-search',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    GridAllModule,
    RegistrationDetailPanelComponent,
    RefundModalComponent,
    BatchEmailModalComponent,
    MobileQuickLookupComponent
  ],
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

  // Search state
  searchRequest = signal<RegistrationSearchRequest>({
    name: '',
    email: '',
    roleId: '',
    teamId: '',
    agegroupId: '',
    divisionId: '',
    clubName: '',
    active: true,
    owesFilter: 'any',
    regDateFrom: undefined,
    regDateTo: undefined,
    skip: 0,
    take: 20,
    sortField: 'lastName',
    sortDirection: 'asc'
  });

  searchResults = signal<RegistrationSearchResponse | null>(null);
  isSearching = signal(false);

  /** Syncfusion DataResult format: { result, count } enables server-side paging */
  gridDataSource = computed(() => {
    const res = this.searchResults();
    if (!res) return { result: [], count: 0 };
    return { result: res.result, count: res.count };
  });

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

  ngOnInit(): void {
    this.checkMobileView();
    window.addEventListener('resize', this.resizeHandler);
    this.loadFilterOptions();
    this.executeSearch();
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.resizeHandler);
  }

  private checkMobileView(): void {
    this.isMobile.set(window.matchMedia('(max-width: 767px)').matches);
  }

  loadFilterOptions(): void {
    this.searchService.getFilterOptions().subscribe({
      next: (options) => this.filterOptions.set(options),
      error: (err) => {
        this.toast.show('Failed to load filter options', 'danger', 4000);
        console.error('Error loading filter options:', err);
      }
    });
  }

  executeSearch(): void {
    this.isSearching.set(true);
    const req = this.sanitizeRequest(this.searchRequest());
    const skip = req.skip ?? 0;
    this.searchService.search(req).subscribe({
      next: (results) => {
        // Stamp row numbers onto each result for the grid template
        const numbered = results.result.map((r, i) => ({ ...r, _rowNum: skip + i + 1 }));
        this.searchResults.set({ ...results, result: numbered });
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
    this.searchRequest.set({
      name: '',
      email: '',
      roleId: '',
      teamId: '',
      agegroupId: '',
      divisionId: '',
      clubName: '',
      active: true,
      owesFilter: 'any',
      regDateFrom: undefined,
      regDateTo: undefined,
      skip: 0,
      take: 20,
      sortField: 'RegistrationTs',
      sortDirection: 'desc'
    });
    this.executeSearch();
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

    // Refresh detail panel if open
    const currentDetail = this.selectedDetail();
    if (currentDetail && this.isPanelOpen()) {
      this.openDetail(currentDetail.registrationId);
    }
  }

  exportExcel(): void {
    if (this.grid) {
      this.grid.excelExport();
    }
  }

  onActionBegin(args: any): void {
    if (args.requestType === 'paging') {
      args.cancel = true;
      const currentRequest = this.searchRequest();
      const pageSize = args.pageSize || 20;
      const currentPage = args.currentPage || 1;
      this.searchRequest.set({
        ...currentRequest,
        skip: (currentPage - 1) * pageSize,
        take: pageSize
      });
      this.executeSearch();
    } else if (args.requestType === 'sorting' && args.columnName) {
      args.cancel = true;
      const currentRequest = this.searchRequest();
      this.searchRequest.set({
        ...currentRequest,
        skip: 0,
        sortField: args.columnName,
        sortDirection: args.direction === 'Descending' ? 'desc' : 'asc'
      });
      this.executeSearch();
    }
  }

  queryCellInfo(args: QueryCellInfoEventArgs): void {
    if (args.column?.field === 'owedTotal') {
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

  /** Convert empty strings to null so Guid? fields don't fail model binding */
  private sanitizeRequest(req: RegistrationSearchRequest): RegistrationSearchRequest {
    return {
      ...req,
      name: req.name || undefined,
      email: req.email || undefined,
      roleId: req.roleId || undefined,
      teamId: req.teamId || undefined,
      agegroupId: req.agegroupId || undefined,
      divisionId: req.divisionId || undefined,
      clubName: req.clubName || undefined,
      owesFilter: req.owesFilter === 'any' ? undefined : req.owesFilter
    };
  }

  // Update filter helpers
  updateName(value: string): void {
    this.searchRequest.update(req => ({ ...req, name: value }));
  }

  updateEmail(value: string): void {
    this.searchRequest.update(req => ({ ...req, email: value }));
  }

  updateRoleId(value: string): void {
    this.searchRequest.update(req => ({ ...req, roleId: value || null }));
  }

  updateTeamId(value: string): void {
    this.searchRequest.update(req => ({ ...req, teamId: value || null }));
  }

  updateAgegroupId(value: string): void {
    this.searchRequest.update(req => ({ ...req, agegroupId: value || null }));
  }

  updateDivisionId(value: string): void {
    this.searchRequest.update(req => ({ ...req, divisionId: value || null }));
  }

  updateClubName(value: string): void {
    this.searchRequest.update(req => ({ ...req, clubName: value }));
  }

  updateActive(value: string): void {
    const active = value === 'all' ? null : value === 'active';
    this.searchRequest.update(req => ({ ...req, active }));
  }

  updateOwesFilter(value: string): void {
    this.searchRequest.update(req => ({ ...req, owesFilter: value || 'any' }));
  }

  updateRegDateFrom(value: string): void {
    this.searchRequest.update(req => ({ ...req, regDateFrom: value || undefined }));
  }

  updateRegDateTo(value: string): void {
    this.searchRequest.update(req => ({ ...req, regDateTo: value || undefined }));
  }
}
