import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { NgClass, DatePipe } from '@angular/common';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { DiscountCodeService } from './services/discount-code.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CodeFormModalComponent } from './components/code-form-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { DiscountCodeDto, BatchUpdateStatusRequest } from '../../../core/api';

@Component({
  selector: 'app-discount-codes',
  standalone: true,
  imports: [NgClass, DatePipe, GridAllModule, CodeFormModalComponent, ConfirmDialogComponent],
  templateUrl: './discount-codes.component.html',
  styleUrl: './discount-codes.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DiscountCodesComponent implements OnInit {
  private readonly discountCodeService = inject(DiscountCodeService);
  private readonly toastService = inject(ToastService);

  // State signals
  codes = signal<DiscountCodeDto[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<DiscountCodeDto | null>(null);
  deleteTarget = signal<DiscountCodeDto | null>(null);

  ngOnInit(): void {
    this.loadDiscountCodes();
  }

  loadDiscountCodes(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.discountCodeService.getDiscountCodes().subscribe({
      next: (codes) => {
        this.codes.set(codes);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.message || 'Failed to load discount codes');
        this.isLoading.set(false);
      }
    });
  }

  // Modal actions
  openAdd(): void {
    this.showAddModal.set(true);
  }

  openEdit(code: DiscountCodeDto): void {
    this.editTarget.set(code);
    this.showEditModal.set(true);
  }

  confirmDelete(code: DiscountCodeDto): void {
    if (code.usageCount > 0) {
      this.toastService.show(`Cannot delete code "${code.codeName}" - it has been used ${code.usageCount} time(s)`, 'danger');
      return;
    }
    this.deleteTarget.set(code);
    this.showDeleteConfirm.set(true);
  }

  onDeleteConfirmed(): void {
    const target = this.deleteTarget();
    if (!target) return;

    this.discountCodeService.deleteDiscountCode(target.ai).subscribe({
      next: () => {
        this.toastService.show(`Discount code "${target.codeName}" deleted`, 'success');
        this.loadDiscountCodes();
        this.showDeleteConfirm.set(false);
        this.deleteTarget.set(null);
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Failed to delete discount code', 'danger');
      }
    });
  }

  onFormSaved(): void {
    this.showAddModal.set(false);
    this.showEditModal.set(false);
    this.editTarget.set(null);
    this.loadDiscountCodes();
  }

  // Bulk activate / deactivate all
  activateAll(): void {
    const allIds = this.codes().map(c => c.ai);
    if (allIds.length === 0) return;
    this.batchUpdateStatus(allIds, true);
  }

  deactivateAll(): void {
    const allIds = this.codes().map(c => c.ai);
    if (allIds.length === 0) return;
    this.batchUpdateStatus(allIds, false);
  }

  private batchUpdateStatus(codeIds: number[], isActive: boolean): void {
    const request: BatchUpdateStatusRequest = {
      codeIds,
      isActive
    };

    this.discountCodeService.batchUpdateStatus(request).subscribe({
      next: (result) => {
        this.toastService.show(`${result.updatedCount} code(s) ${isActive ? 'activated' : 'deactivated'}`, 'success');
        this.loadDiscountCodes();
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Batch update failed', 'danger');
      }
    });
  }

  // UI helpers
  getDiscountTypeBadgeClass(type: string): string {
    return type === 'Percentage' ? 'bg-info-subtle text-info-emphasis' : 'bg-success-subtle text-success-emphasis';
  }

  getDiscountTypeSymbol(type: string): string {
    return type === 'Percentage' ? '%' : '$';
  }

  getExpirationChipClass(code: DiscountCodeDto): string {
    if (code.isExpired) return 'bg-danger-subtle text-danger-emphasis';

    const now = new Date();
    const endDate = new Date(code.endDate);
    const daysUntilExpiration = Math.floor((endDate.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));

    if (daysUntilExpiration < 7) return 'bg-warning-subtle text-warning-emphasis';
    return 'bg-success-subtle text-success-emphasis';
  }

  getExpirationText(code: DiscountCodeDto): string {
    if (code.isExpired) return 'Expired';

    const now = new Date();
    const endDate = new Date(code.endDate);
    const daysUntilExpiration = Math.floor((endDate.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));

    if (daysUntilExpiration < 7) return `${daysUntilExpiration}d left`;
    return 'Active';
  }
}
