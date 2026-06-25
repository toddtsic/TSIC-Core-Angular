import { ChangeDetectionStrategy, Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { GridAllModule, GridComponent, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { BulletinAdminService } from './services/bulletin-admin.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { BulletinFormModalComponent } from './components/bulletin-form-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { BulletinAdminDto } from '../../../core/api';

@Component({
  selector: 'app-bulletin-editor',
  standalone: true,
  imports: [GridAllModule, BulletinFormModalComponent, ConfirmDialogComponent],
  templateUrl: './bulletin-editor.component.html',
  styleUrl: './bulletin-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinEditorComponent implements OnInit {
  private readonly bulletinService = inject(BulletinAdminService);
  private readonly toastService = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly grid = viewChild.required<GridComponent>('grid');

  // Deep-link target: ?edit={bulletinId} (e.g. the pencil on the public job-home
  // bulletins). Consumed once after the first load, then cleared from the URL.
  private pendingEditId: string | null = null;

  // When the deep-link arrived with ?from=home, the edit originated from the
  // public job-home bulletin (not the admin grid). Once that edit is finished —
  // saved or closed — route back to job-home rather than landing on the grid.
  private returnToHome = false;

  // State signals
  bulletins = signal<BulletinAdminDto[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Bulletin id whose active toggle is mid-flight (disables that row's toggle).
  togglingId = signal<string | null>(null);

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<BulletinAdminDto | null>(null);
  deleteTarget = signal<BulletinAdminDto | null>(null);

  // Grid settings
  sortSettings: SortSettingsModel = { columns: [{ field: 'modified', direction: 'Descending' }] };

  ngOnInit(): void {
    this.pendingEditId = this.route.snapshot.queryParamMap.get('edit');
    this.returnToHome = this.route.snapshot.queryParamMap.get('from') === 'home';
    this.loadBulletins();
  }

  loadBulletins(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.bulletinService.getAllBulletins().subscribe({
      next: (bulletins) => {
        this.bulletins.set(bulletins);
        this.isLoading.set(false);
        this.openPendingEditIfRequested(bulletins);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to load bulletins');
        this.isLoading.set(false);
      }
    });
  }

  // Row click → edit
  onRowSelected(args: any): void {
    if (args.data) {
      this.openEdit(args.data as BulletinAdminDto);
    }
  }

  // Row numbers
  refreshRowNumbers(): void {
    const grid = this.grid();
    if (!grid) return;
    const rows = grid.getRows();
    const page = grid.pageSettings?.currentPage ?? 1;
    const size = grid.pageSettings?.pageSize ?? rows.length;
    const offset = (page - 1) * size;
    rows.forEach((row, i) => {
      const cell = row.querySelector('td');
      if (cell) cell.textContent = String(offset + i + 1);
    });
  }

  onActionComplete(args: any): void {
    if (args.requestType === 'sorting' || args.requestType === 'paging') {
      this.refreshRowNumbers();
    }
  }

  // Modal actions
  openAdd(): void {
    this.showAddModal.set(true);
  }

  openEdit(bulletin: BulletinAdminDto): void {
    this.editTarget.set(bulletin);
    this.showEditModal.set(true);
  }

  /** Honor a ?edit={id} deep-link once: open that bulletin's modal, then clear the param. */
  private openPendingEditIfRequested(bulletins: BulletinAdminDto[]): void {
    const editId = this.pendingEditId;
    if (!editId) return;
    this.pendingEditId = null;
    // Drop the query param so a later reload/save doesn't reopen the modal.
    this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    const target = bulletins.find(b => b.bulletinId === editId);
    if (target) {
      this.openEdit(target);
    }
  }

  /** Inline active toggle: flip status immediately, persist, revert on failure. */
  toggleActive(bulletin: BulletinAdminDto): void {
    if (this.togglingId()) return;
    const next = !bulletin.active;
    this.togglingId.set(bulletin.bulletinId);
    // Optimistic: swap the row to its new state up front (new array, new object).
    this.applyActive(bulletin.bulletinId, next);
    this.bulletinService.setBulletinActive(bulletin.bulletinId, next).subscribe({
      next: () => {
        this.togglingId.set(null);
        this.toastService.show(`Bulletin ${next ? 'activated' : 'deactivated'}`, 'success');
      },
      error: (error) => {
        this.applyActive(bulletin.bulletinId, !next); // revert
        this.togglingId.set(null);
        this.toastService.show(error.error?.message || 'Could not update status', 'danger');
      }
    });
  }

  private applyActive(bulletinId: string, active: boolean): void {
    this.bulletins.set(
      this.bulletins().map(b => b.bulletinId === bulletinId ? { ...b, active } : b)
    );
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
    if (this.returnToHome) {
      this.goHome();
      return;
    }
    this.loadBulletins();
  }

  /** Edit modal dismissed without saving. */
  onEditClosed(): void {
    this.showEditModal.set(false);
    this.editTarget.set(null);
    if (this.returnToHome) {
      this.goHome();
    }
  }

  /** Return to the public job-home that launched this edit. */
  private goHome(): void {
    this.returnToHome = false;
    let r: ActivatedRoute | null = this.route;
    let jobPath: string | null = null;
    while (r && !jobPath) {
      jobPath = r.snapshot.paramMap.get('jobPath');
      r = r.parent;
    }
    if (jobPath) {
      this.router.navigate(['/', jobPath]);
    }
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
