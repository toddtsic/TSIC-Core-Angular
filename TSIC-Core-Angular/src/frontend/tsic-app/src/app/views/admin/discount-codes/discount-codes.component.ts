import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DiscountCodeService } from './services/discount-code.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CodeFormModalComponent } from './components/code-form-modal.component';
import { BulkCodeModalComponent } from './components/bulk-code-modal.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { DiscountCodeDto, BatchUpdateStatusRequest } from '../../../core/api';

@Component({
  selector: 'app-discount-codes',
  standalone: true,
  imports: [CommonModule, CodeFormModalComponent, BulkCodeModalComponent, ConfirmDialogComponent],
  templateUrl: './discount-codes.component.html',
  styleUrl: './discount-codes.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DiscountCodesComponent implements OnInit {
  private readonly discountCodeService = inject(DiscountCodeService);
  private readonly toastService = inject(ToastService);

  // State signals
  codes = signal<DiscountCodeDto[]>([]);
  selectedIds = signal<Set<number>>(new Set());
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showBulkModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<DiscountCodeDto | null>(null);
  deleteTarget = signal<DiscountCodeDto | null>(null);

  // Computed properties
  hasSelection = computed(() => this.selectedIds().size > 0);
  selectedCount = computed(() => this.selectedIds().size);
  hasUsedCodesSelected = computed(() => {
    const selected = this.selectedIds();
    return this.codes().some(code => selected.has(code.ai) && code.usageCount > 0);
  });
  allSelectableSelected = computed(() => {
    const codes = this.codes();
    const selected = this.selectedIds();
    return codes.length > 0 && codes.every(code => selected.has(code.ai));
  });

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

  // Selection methods
  toggleSelect(code: DiscountCodeDto): void {
    const selected = new Set(this.selectedIds());
    if (selected.has(code.ai)) {
      selected.delete(code.ai);
    } else {
      selected.add(code.ai);
    }
    this.selectedIds.set(selected);
  }

  toggleSelectAll(): void {
    if (this.allSelectableSelected()) {
      this.selectedIds.set(new Set());
    } else {
      const allIds = this.codes().map(c => c.ai);
      this.selectedIds.set(new Set(allIds));
    }
  }

  isSelected(code: DiscountCodeDto): boolean {
    return this.selectedIds().has(code.ai);
  }

  // Modal actions
  openAdd(): void {
    this.showAddModal.set(true);
  }

  openEdit(code: DiscountCodeDto): void {
    this.editTarget.set(code);
    this.showEditModal.set(true);
  }

  openBulk(): void {
    this.showBulkModal.set(true);
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
    this.showBulkModal.set(false);
    this.editTarget.set(null);
    this.loadDiscountCodes();
  }

  // Batch operations
  openBatchConfirm(action: 'activate' | 'inactivate' | 'delete'): void {
    const count = this.selectedCount();
    const selected = Array.from(this.selectedIds());

    if (action === 'delete') {
      if (this.hasUsedCodesSelected()) {
        this.toastService.show('Cannot delete codes that have been used', 'danger');
        return;
      }
      // Show confirmation for batch delete
      if (confirm(`Delete ${count} discount code(s)? This cannot be undone.`)) {
        this.batchDelete(selected);
      }
    } else {
      const isActive = action === 'activate';
      this.batchUpdateStatus(selected, isActive);
    }
  }

  batchUpdateStatus(codeIds: number[], isActive: boolean): void {
    const request: BatchUpdateStatusRequest = {
      codeIds,
      isActive
    };

    this.discountCodeService.batchUpdateStatus(request).subscribe({
      next: (result) => {
        this.toastService.show(`${result.updatedCount} code(s) ${isActive ? 'activated' : 'deactivated'}`, 'success');
        this.selectedIds.set(new Set());
        this.loadDiscountCodes();
      },
      error: (error) => {
        this.toastService.show(error.error?.message || 'Batch update failed', 'danger');
      }
    });
  }

  batchDelete(codeIds: number[]): void {
    let deleteCount = 0;
    let errorCount = 0;

    codeIds.forEach(codeId => {
      this.discountCodeService.deleteDiscountCode(codeId).subscribe({
        next: () => {
          deleteCount++;
          if (deleteCount + errorCount === codeIds.length) {
            this.finishBatchDelete(deleteCount, errorCount);
          }
        },
        error: () => {
          errorCount++;
          if (deleteCount + errorCount === codeIds.length) {
            this.finishBatchDelete(deleteCount, errorCount);
          }
        }
      });
    });
  }

  finishBatchDelete(deleteCount: number, errorCount: number): void {
    if (errorCount === 0) {
      this.toastService.show(`${deleteCount} code(s) deleted`, 'success');
    } else {
      this.toastService.show(`${deleteCount} deleted, ${errorCount} failed`, 'warning');
    }
    this.selectedIds.set(new Set());
    this.loadDiscountCodes();
  }

  // UI helpers
  getDiscountTypeBadgeClass(type: string): string {
    return type === 'Percentage' ? 'bg-info-subtle text-info-emphasis' : 'bg-success-subtle text-success-emphasis';
  }

  getDiscountTypeSymbol(type: string): string {
    return type === 'Percentage' ? '%' : '$';
  }

  getUsageProgressColor(code: DiscountCodeDto): string {
    const usageCount = code.usageCount;
    if (usageCount === 0) return 'bg-secondary';
    return 'bg-primary';
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
