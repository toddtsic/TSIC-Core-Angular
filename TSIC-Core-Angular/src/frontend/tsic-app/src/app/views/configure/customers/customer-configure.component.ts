import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CustomerConfigureService } from './customer-configure.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CustomerDialogComponent } from './customer-dialog/customer-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { CustomerListDto, TimezoneDto } from '../../../core/api';

type SortColumn = 'customerAi' | 'customerName' | 'timezoneName' | 'jobCount';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-customer-configure',
  standalone: true,
  imports: [CommonModule, CustomerDialogComponent, ConfirmDialogComponent],
  templateUrl: './customer-configure.component.html',
  styleUrl: './customer-configure.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomerConfigureComponent implements OnInit {
  private readonly svc = inject(CustomerConfigureService);
  private readonly toast = inject(ToastService);

  // Data signals
  customers = signal<CustomerListDto[]>([]);
  timezones = signal<TimezoneDto[]>([]);

  // UI state
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Sort state
  sortColumn = signal<SortColumn>('customerName');
  sortDirection = signal<SortDirection>('asc');

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<CustomerListDto | null>(null);
  deleteTarget = signal<CustomerListDto | null>(null);

  // Sorted customers
  sortedCustomers = computed(() => {
    const list = [...this.customers()];
    const col = this.sortColumn();
    const dir = this.sortDirection();
    const mult = dir === 'asc' ? 1 : -1;

    list.sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'customerAi':
          cmp = a.customerAi - b.customerAi;
          break;
        case 'customerName':
          cmp = (a.customerName ?? '').localeCompare(b.customerName ?? '');
          break;
        case 'timezoneName':
          cmp = (a.timezoneName ?? '').localeCompare(b.timezoneName ?? '');
          break;
        case 'jobCount':
          cmp = a.jobCount - b.jobCount;
          break;
      }
      return cmp * mult;
    });

    return list;
  });

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.svc.getAll().subscribe({
      next: (customers) => {
        this.customers.set(customers);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load customers');
        this.isLoading.set(false);
      }
    });

    // Load timezones once for the dialog dropdowns
    this.svc.getTimezones().subscribe({
      next: (tz) => this.timezones.set(tz)
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

  openEdit(customer: CustomerListDto): void {
    this.editTarget.set(customer);
    this.showEditModal.set(true);
  }

  confirmDelete(customer: CustomerListDto): void {
    if (customer.jobCount > 0) {
      this.toast.show(`Cannot delete "${customer.customerName}" — it has ${customer.jobCount} associated job(s)`, 'danger');
      return;
    }
    this.deleteTarget.set(customer);
    this.showDeleteConfirm.set(true);
  }

  onDeleteConfirmed(): void {
    const target = this.deleteTarget();
    if (!target) return;

    this.svc.delete(target.customerId).subscribe({
      next: () => {
        this.toast.show(`Customer "${target.customerName}" deleted`, 'success');
        this.loadData();
        this.showDeleteConfirm.set(false);
        this.deleteTarget.set(null);
      },
      error: (err) => {
        this.toast.show(err.error?.message || 'Failed to delete customer', 'danger');
      }
    });
  }

  onFormSaved(): void {
    this.showAddModal.set(false);
    this.showEditModal.set(false);
    this.editTarget.set(null);
    this.loadData();
  }
}
