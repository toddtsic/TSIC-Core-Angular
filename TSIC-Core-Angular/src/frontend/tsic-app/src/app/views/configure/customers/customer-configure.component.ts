import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { CustomerConfigureService } from './customer-configure.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CustomerDialogComponent } from './customer-dialog/customer-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { CustomerListDto, TimezoneDto } from '../../../core/api';

@Component({
  selector: 'app-customer-configure',
  standalone: true,
  imports: [NgClass, GridAllModule, CustomerDialogComponent, ConfirmDialogComponent],
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

  // Modal state
  showAddModal = signal(false);
  showEditModal = signal(false);
  showDeleteConfirm = signal(false);
  editTarget = signal<CustomerListDto | null>(null);
  deleteTarget = signal<CustomerListDto | null>(null);

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

    this.svc.getTimezones().subscribe({
      next: (tz) => this.timezones.set(tz)
    });
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
