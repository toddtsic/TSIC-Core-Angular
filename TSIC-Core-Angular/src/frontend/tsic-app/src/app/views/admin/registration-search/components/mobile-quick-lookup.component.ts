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
  visibleCount = signal<number>(20);

  private allResults: RegistrationSearchResultDto[] = [];
  readonly pageSize = 20;
  private searchTimeout: any = null;

  onSearchInput(): void {
    if (this.searchTimeout) { clearTimeout(this.searchTimeout); }
    this.searchTimeout = setTimeout(() => { this.executeSearch(); }, 400);
  }

  executeSearch(): void {
    const search = this.searchText();
    if (!search.trim()) { this.allResults = []; this.results.set([]); this.totalCount.set(0); return; }

    this.isSearching.set(true);
    this.visibleCount.set(this.pageSize);

    this.searchService.search({ name: search }).subscribe({
      next: (response) => {
        this.isSearching.set(false);
        this.allResults = response.result;
        this.totalCount.set(response.count);
        this.results.set(this.allResults.slice(0, this.visibleCount()));
      },
      error: (err) => {
        this.isSearching.set(false);
        this.toast.show(`Search failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
      }
    });
  }

  loadMore(): void {
    const newCount = this.visibleCount() + this.pageSize;
    this.visibleCount.set(newCount);
    this.results.set(this.allResults.slice(0, newCount));
  }

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
