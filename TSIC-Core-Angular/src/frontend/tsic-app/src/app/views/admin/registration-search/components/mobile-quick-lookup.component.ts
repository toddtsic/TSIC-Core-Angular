import { Component, ChangeDetectionStrategy, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { RegistrationSearchResultDto, RegistrationDetailDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

@Component({
  selector: 'app-mobile-quick-lookup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './mobile-quick-lookup.component.html',
  styleUrl: './mobile-quick-lookup.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MobileQuickLookupComponent {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  searchText = signal<string>('');
  results = signal<RegistrationSearchResultDto[]>([]);
  totalCount = signal<number>(0);
  isSearching = signal<boolean>(false);
  expandedId = signal<string | null>(null);
  expandedDetail = signal<RegistrationDetailDto | null>(null);
  currentPage = signal<number>(1);

  readonly pageSize = 20;
  private searchTimeout: any = null;

  onSearchInput(): void {
    if (this.searchTimeout) { clearTimeout(this.searchTimeout); }
    this.searchTimeout = setTimeout(() => { this.currentPage.set(1); this.executeSearch(); }, 400);
  }

  executeSearch(): void {
    const search = this.searchText();
    if (!search.trim()) { this.results.set([]); this.totalCount.set(0); return; }

    this.isSearching.set(true);
    const page = this.currentPage();

    this.searchService.search({
      name: search,
      skip: (page - 1) * this.pageSize,
      take: this.pageSize
    }).subscribe({
      next: (response) => {
        this.isSearching.set(false);
        if (page === 1) { this.results.set(response.result); }
        else { this.results.set([...this.results(), ...response.result]); }
        this.totalCount.set(response.count);
      },
      error: (err) => {
        this.isSearching.set(false);
        this.toast.show(`Search failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
      }
    });
  }

  loadMore(): void { this.currentPage.set(this.currentPage() + 1); this.executeSearch(); }

  toggleExpand(registrationId: string): void {
    if (this.expandedId() === registrationId) { this.expandedId.set(null); this.expandedDetail.set(null); return; }
    this.expandedId.set(registrationId);
    this.expandedDetail.set(null);
    this.searchService.getRegistrationDetail(registrationId).subscribe({
      next: (detail) => { this.expandedDetail.set(detail); },
      error: (err) => {
        this.toast.show(`Failed to load details: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
        this.expandedId.set(null);
      }
    });
  }

  hasMoreResults(): boolean { return this.results().length < this.totalCount(); }
}
