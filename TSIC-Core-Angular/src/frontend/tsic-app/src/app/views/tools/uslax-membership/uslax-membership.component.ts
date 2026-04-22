import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { DatePipe, NgClass } from '@angular/common';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { UsLaxMembershipService } from '@infrastructure/services/uslax-membership.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import type { UsLaxReconciliationCandidateDto, UsLaxReconciliationRowDto } from '@core/api';

@Component({
	selector: 'app-uslax-membership',
	standalone: true,
	imports: [DatePipe, NgClass, GridAllModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-membership.component.html',
	styleUrl: './uslax-membership.component.scss'
})
export class UsLaxMembershipComponent implements OnInit {
	private readonly service = inject(UsLaxMembershipService);
	private readonly jobService = inject(JobService);
	private readonly toast = inject(ToastService);

	readonly candidates = signal<UsLaxReconciliationCandidateDto[]>([]);
	readonly rows = signal<UsLaxReconciliationRowDto[]>([]);
	readonly isLoadingCandidates = signal(false);
	readonly isReconciling = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly summary = signal<{ totalPinged: number; datesUpdated: number; failed: number } | null>(null);

	readonly jobValidThrough = computed(() => {
		const raw = this.jobService.currentJob()?.usLaxNumberValidThroughDate;
		if (!raw) return null;
		const d = new Date(raw);
		return isNaN(d.getTime()) ? null : d;
	});

	readonly canReconcile = computed(() => this.candidates().length > 0 && !this.isReconciling());

	ngOnInit(): void {
		this.loadCandidates();
	}

	loadCandidates(): void {
		this.isLoadingCandidates.set(true);
		this.errorMessage.set(null);
		this.service.getCandidates().subscribe({
			next: list => {
				this.candidates.set(list);
				this.isLoadingCandidates.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load candidates.');
				this.isLoadingCandidates.set(false);
			}
		});
	}

	reconcileAll(): void {
		if (!this.canReconcile()) return;
		this.isReconciling.set(true);
		this.rows.set([]);
		this.summary.set(null);
		this.service.reconcile({}).subscribe({
			next: response => {
				this.rows.set(response.rows);
				this.summary.set({
					totalPinged: response.totalPinged,
					datesUpdated: response.datesUpdated,
					failed: response.failed
				});
				this.isReconciling.set(false);
				const msg = `Reconciled ${response.totalPinged}. ${response.datesUpdated} expiry date(s) updated. ${response.failed} failed.`;
				this.toast.show(msg, response.failed > 0 ? 'warning' : 'success', 5000);
				// Refresh candidate set so the current-expiry column reflects writes.
				this.loadCandidates();
			},
			error: err => {
				this.isReconciling.set(false);
				this.toast.show(`Reconciliation failed: ${err?.error?.message || 'Unknown error'}`, 'danger', 5000);
			}
		});
	}

	rowClass(row: UsLaxReconciliationRowDto): string {
		if (row.statusCode !== 200) return 'row-error';
		if (row.expiryDateUpdated) return 'row-updated';
		if (row.memStatus === 'Inactive') return 'row-inactive';
		return '';
	}

	involvementBadges(row: UsLaxReconciliationRowDto): string[] {
		const inv = row.involvement;
		if (!Array.isArray(inv)) return [];
		return inv.filter((x): x is string => typeof x === 'string');
	}

	ageVerifiedDisplay(row: UsLaxReconciliationRowDto): string {
		const v = row.ageVerified;
		if (v == null) return '';
		return v.toString().toLowerCase() === 'true' ? 'Yes' : 'No';
	}
}
