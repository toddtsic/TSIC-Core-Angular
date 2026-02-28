import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgeRangeAdminService } from './services/age-range-admin.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { AgeRangeFormModalComponent } from './components/age-range-form-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { AgeRangeDto } from '../../../core/api';

type SortColumn = 'rangeName' | 'rangeLeft' | 'rangeRight' | 'modified';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-configure-age-ranges',
  standalone: true,
  imports: [CommonModule, AgeRangeFormModalComponent, ConfirmDialogComponent],
  templateUrl: './configure-age-ranges.component.html',
  styleUrl: './configure-age-ranges.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConfigureAgeRangesComponent implements OnInit {
  private readonly ageRangeService = inject(AgeRangeAdminService);
  private readonly toastService = inject(ToastService);

  // State signals
  ageRanges = signal<AgeRangeDto[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Sort state
  sortColumn = signal<SortColumn>('rangeLeft');
  sortDirection = signal<SortDirection>('asc');

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<AgeRangeDto | null>(null);
  deleteTarget = signal<AgeRangeDto | null>(null);

  // Sorted age ranges
  sortedAgeRanges = computed(() => {
    const list = [...this.ageRanges()];
    const col = this.sortColumn();
    const dir = this.sortDirection();
    const mult = dir === 'asc' ? 1 : -1;

    list.sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'rangeName':
          cmp = (a.rangeName ?? '').localeCompare(b.rangeName ?? '');
          break;
        case 'rangeLeft':
          cmp = new Date(a.rangeLeft).getTime() - new Date(b.rangeLeft).getTime();
          break;
        case 'rangeRight':
          cmp = new Date(a.rangeRight).getTime() - new Date(b.rangeRight).getTime();
          break;
        case 'modified':
          cmp = new Date(a.modified).getTime() - new Date(b.modified).getTime();
          break;
      }
      return cmp * mult;
    });

    return list;
  });

  ngOnInit(): void {
    this.loadAgeRanges();
  }

  loadAgeRanges(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.ageRangeService.getAllAgeRanges().subscribe({
      next: (ranges) => {
        this.ageRanges.set(ranges);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to load age ranges');
        this.isLoading.set(false);
      }
    });
  }

  // Sorting
  toggleSort(column: SortColumn): void {
    if (this.sortColumn() === column) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
  }

  getSortIcon(column: SortColumn): string {
    if (this.sortColumn() !== column) return 'bi-chevron-expand';
    return this.sortDirection() === 'asc' ? 'bi-sort-up' : 'bi-sort-down';
  }

  // Modal actions
  openAdd(): void {
    this.showAddModal.set(true);
  }

  openEdit(range: AgeRangeDto): void {
    this.editTarget.set(range);
    this.showEditModal.set(true);
  }

  confirmDelete(range: AgeRangeDto): void {
    this.deleteTarget.set(range);
    this.showDeleteConfirm.set(true);
  }

  onDeleteConfirmed(): void {
    const target = this.deleteTarget();
    if (!target) return;

    this.ageRangeService.deleteAgeRange(target.ageRangeId).subscribe({
      next: () => {
        this.toastService.show(`Age range "${target.rangeName}" deleted`, 'success');
        this.loadAgeRanges();
        this.showDeleteConfirm.set(false);
        this.deleteTarget.set(null);
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Failed to delete age range', 'danger');
      }
    });
  }

  onFormSaved(): void {
    this.showAddModal.set(false);
    this.showEditModal.set(false);
    this.editTarget.set(null);
    this.loadAgeRanges();
  }

  // UI helpers
  formatDate(date: string | null | undefined): string {
    if (!date) return '—';
    return new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  getRelativeTime(date: string): string {
    const now = new Date();
    const then = new Date(date);
    const diffMs = now.getTime() - then.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'today';
    if (diffDays === 1) return 'yesterday';
    if (diffDays < 30) return `${diffDays}d ago`;
    if (diffDays < 365) return `${Math.floor(diffDays / 30)}mo ago`;
    return `${Math.floor(diffDays / 365)}y ago`;
  }
}
