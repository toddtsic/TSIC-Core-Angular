import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { CustomerConfigureService } from './customer-configure.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { CustomerDialogComponent } from './customer-dialog/customer-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { CustomerListDto } from '../../../core/api';

// AM-049: a customer is "dormant" once their most-recent active job is older than
// this many years (Ann: declutter long-inactive customers like Black Diamond, 2023).
const DORMANT_AFTER_YEARS = 2;

type Segment = 'active' | 'dormant' | 'all';

@Component({
  selector: 'app-customer-configure',
  standalone: true,
  imports: [DatePipe, GridAllModule, CustomerDialogComponent, ConfirmDialogComponent],
  templateUrl: './customer-configure.component.html',
  styleUrl: './customer-configure.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomerConfigureComponent implements OnInit {
  private readonly svc = inject(CustomerConfigureService);
  private readonly toast = inject(ToastService);

  // Data signals
  customers = signal<CustomerListDto[]>([]);

  // Segment filter — default to recently-active customers (a job within the window).
  segment = signal<Segment>('active');

  // How many years of inactivity marks a customer dormant — surfaced in the UI copy.
  readonly dormantAfterYears = DORMANT_AFTER_YEARS;

  // Cutoff = today minus the dormancy window. Computed (no signal deps) so it is
  // evaluated once per session and reused across the counts/filter below.
  readonly cutoffDate = computed(() => {
    const d = new Date();
    d.setFullYear(d.getFullYear() - DORMANT_AFTER_YEARS);
    return d;
  });

  // "Active" = most-recent active job falls on/after the cutoff. Older-than-cutoff
  // OR no jobs at all (null date) counts as "dormant".
  private isRecentlyActive(c: CustomerListDto, cutoff: Date): boolean {
    return !!c.lastActiveJobDate && new Date(c.lastActiveJobDate) >= cutoff;
  }

  readonly activeCount = computed(() => {
    const cutoff = this.cutoffDate();
    return this.customers().filter(c => this.isRecentlyActive(c, cutoff)).length;
  });
  readonly dormantCount = computed(() => {
    const cutoff = this.cutoffDate();
    return this.customers().filter(c => !this.isRecentlyActive(c, cutoff)).length;
  });

  // The segment strip only earns its place when both buckets are non-empty —
  // otherwise the filter is noise.
  readonly showSegments = computed(() => this.activeCount() > 0 && this.dormantCount() > 0);

  // Derived, not reset imperatively: if the strip is hidden while segment() is
  // stranded on a now-empty bucket, filtering falls back to 'all'.
  readonly effectiveSegment = computed<Segment>(() => this.showSegments() ? this.segment() : 'all');

  readonly filteredCustomers = computed(() => {
    const seg = this.effectiveSegment();
    const cutoff = this.cutoffDate();
    const all = this.customers();
    if (seg === 'active') return all.filter(c => this.isRecentlyActive(c, cutoff));
    if (seg === 'dormant') return all.filter(c => !this.isRecentlyActive(c, cutoff));
    return all;
  });

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
  }

  setSegment(seg: Segment): void {
    this.segment.set(seg);
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

  onAddSaved(): void {
    // A new customer has no jobs yet → it lands in the Dormant bucket; jump there so
    // it stays visible instead of vanishing from the default Active view.
    this.segment.set('dormant');
    this.onFormSaved();
  }
}
