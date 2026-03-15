import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BulletinAdminService } from './services/bulletin-admin.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { BulletinFormModalComponent } from './components/bulletin-form-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { BulletinAdminDto } from '../../../core/api';

type SortColumn = 'title' | 'active' | 'startDate' | 'endDate' | 'createDate' | 'modified';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-bulletin-editor',
  standalone: true,
  imports: [CommonModule, BulletinFormModalComponent, ConfirmDialogComponent],
  templateUrl: './bulletin-editor.component.html',
  styleUrl: './bulletin-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinEditorComponent implements OnInit {
  private readonly bulletinService = inject(BulletinAdminService);
  private readonly toastService = inject(ToastService);

  // State signals
  bulletins = signal<BulletinAdminDto[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Sort state
  sortColumn = signal<SortColumn>('createDate');
  sortDirection = signal<SortDirection>('desc');

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<BulletinAdminDto | null>(null);
  deleteTarget = signal<BulletinAdminDto | null>(null);

  // Sorted bulletins
  sortedBulletins = computed(() => {
    const list = [...this.bulletins()];
    const col = this.sortColumn();
    const dir = this.sortDirection();
    const mult = dir === 'asc' ? 1 : -1;

    list.sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'title':
          cmp = (a.title ?? '').localeCompare(b.title ?? '');
          break;
        case 'active':
          cmp = (a.active === b.active) ? 0 : a.active ? -1 : 1;
          break;
        case 'startDate':
          cmp = new Date(a.startDate ?? 0).getTime() - new Date(b.startDate ?? 0).getTime();
          break;
        case 'endDate':
          cmp = new Date(a.endDate ?? 0).getTime() - new Date(b.endDate ?? 0).getTime();
          break;
        case 'createDate':
          cmp = new Date(a.createDate).getTime() - new Date(b.createDate).getTime();
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
    this.loadBulletins();
  }

  loadBulletins(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.bulletinService.getAllBulletins().subscribe({
      next: (bulletins) => {
        this.bulletins.set(bulletins);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to load bulletins');
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

  openEdit(bulletin: BulletinAdminDto): void {
    this.editTarget.set(bulletin);
    this.showEditModal.set(true);
  }

  confirmDelete(bulletin: BulletinAdminDto): void {
    this.deleteTarget.set(bulletin);
    this.showDeleteConfirm.set(true);
  }

  onDeleteConfirmed(): void {
    const target = this.deleteTarget();
    if (!target) return;

    this.bulletinService.deleteBulletin(target.bulletinId).subscribe({
      next: () => {
        this.toastService.show(`Bulletin "${target.title}" deleted`, 'success');
        this.loadBulletins();
        this.showDeleteConfirm.set(false);
        this.deleteTarget.set(null);
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Failed to delete bulletin', 'danger');
      }
    });
  }

  onFormSaved(): void {
    this.showAddModal.set(false);
    this.showEditModal.set(false);
    this.editTarget.set(null);
    this.loadBulletins();
  }

  // Bulk activate / deactivate all
  activateAll(): void {
    if (this.bulletins().length === 0) return;
    this.batchUpdateStatus(true);
  }

  deactivateAll(): void {
    if (this.bulletins().length === 0) return;
    this.batchUpdateStatus(false);
  }

  private batchUpdateStatus(active: boolean): void {
    this.bulletinService.batchUpdateStatus({ active }).subscribe({
      next: (result) => {
        this.toastService.show(`${result.updatedCount} bulletin(s) ${active ? 'activated' : 'deactivated'}`, 'success');
        this.loadBulletins();
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Batch update failed', 'danger');
      }
    });
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

  truncateTitle(title: string | null | undefined): string {
    if (!title) return '(untitled)';
    return title.length > 60 ? title.substring(0, 57) + '...' : title;
  }
}
