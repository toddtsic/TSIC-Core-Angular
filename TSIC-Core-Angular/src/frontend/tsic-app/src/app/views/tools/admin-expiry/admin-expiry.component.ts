import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminExpiryService } from './services/admin-expiry.service';
import { ToastService } from '@shared-ui/toast.service';
import type { AdminExpiryCustomerDto } from '@core/api';

/**
 * SuperUser Admin Expiry tool (migrated from legacy AdminExpiryController):
 * lists every job whose admin door (ExpiryAdmin) has closed, grouped by
 * customer, and updates a job's ExpiryAdmin without entering that job.
 */
@Component({
	selector: 'app-admin-expiry',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './admin-expiry.component.html',
	styleUrl: './admin-expiry.component.scss',
})
export class AdminExpiryComponent implements OnInit {
	private readonly adminExpiryService = inject(AdminExpiryService);
	private readonly toast = inject(ToastService);

	// ── State ──
	readonly customers = signal<AdminExpiryCustomerDto[]>([]);
	readonly loading = signal(false);
	readonly saving = signal(false);
	readonly selectedCustomerId = signal('');
	readonly selectedJobId = signal('');
	readonly newExpiry = signal(''); // yyyy-MM-dd

	// ── Derivations ──
	readonly selectedCustomer = computed(
		() => this.customers().find(c => c.customerId === this.selectedCustomerId()) ?? null);
	readonly jobs = computed(() => this.selectedCustomer()?.jobs ?? []);
	readonly selectedJob = computed(
		() => this.jobs().find(j => j.jobId === this.selectedJobId()) ?? null);
	readonly canSave = computed(
		() => !!this.selectedJobId() && !!this.newExpiry() && !this.saving());

	ngOnInit(): void {
		this.loadExpiredJobs();
	}

	onCustomerChange(customerId: string): void {
		this.selectedCustomerId.set(customerId);
		this.selectedJobId.set('');
		this.newExpiry.set('');
	}

	onJobChange(jobId: string): void {
		this.selectedJobId.set(jobId);
		this.newExpiry.set('');
	}

	save(): void {
		const job = this.selectedJob();
		const expiry = this.newExpiry();
		if (!job || !expiry || this.saving()) return;

		this.saving.set(true);
		this.adminExpiryService.updateExpiry(job.jobId, { expiryAdmin: expiry }).subscribe({
			next: () => {
				this.saving.set(false);
				this.toast.show(`'${job.jobName}' admin expiry updated to ${this.formatDate(expiry)}.`, 'success');
				this.selectedCustomerId.set('');
				this.selectedJobId.set('');
				this.newExpiry.set('');
				this.loadExpiredJobs();
			},
			error: () => {
				this.saving.set(false);
				this.toast.show('Failed to update admin expiry.', 'danger');
			},
		});
	}

	private loadExpiredJobs(): void {
		this.loading.set(true);
		this.adminExpiryService.getExpiredJobs().subscribe({
			next: customers => {
				this.customers.set(customers);
				this.loading.set(false);
			},
			error: () => {
				this.loading.set(false);
				this.toast.show('Failed to load expired jobs.', 'danger');
			},
		});
	}

	private formatDate(isoDate: string): string {
		const [y, m, d] = isoDate.split('-');
		return `${Number(m)}/${Number(d)}/${y}`;
	}
}
