import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { AgeRangeAdminService } from './services/age-range-admin.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { AgeRangeFormModalComponent } from './components/age-range-form-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { AgeRangeDto } from '../../../core/api';

@Component({
  selector: 'app-configure-age-ranges',
  standalone: true,
  imports: [GridAllModule, AgeRangeFormModalComponent, ConfirmDialogComponent],
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

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<AgeRangeDto | null>(null);
  deleteTarget = signal<AgeRangeDto | null>(null);

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

  onRowDoubleClick(args: any): void {
    if (args.rowData) {
      this.openEdit(args.rowData);
    }
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
