import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { ThirdPartyAccessService } from './services/third-party-access.service';
import type { ThirdPartyAccessOverviewDto, ThirdPartyJobRowDto } from '@core/api';

/**
 * "3rd Party Data Access" (SU + SuperDirector): the customer's own console for
 * managing which authorized third party may pull its rosters/schedule export.
 * Reuse-only — the vendor picker offers only accounts that have already held
 * export access with this organization; first-time logins are not created here.
 * Grant = create-or-reactivate; disable = one click. Both are single-click —
 * the standing on-page warning carries the minors'-data caution.
 */
@Component({
	selector: 'app-third-party-access',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './third-party-access.component.html',
	styleUrl: './third-party-access.component.scss',
})
export class ThirdPartyAccessComponent implements OnInit {
	private readonly accessService = inject(ThirdPartyAccessService);
	private readonly toast = inject(ToastService);

	// ── State ──
	readonly overview = signal<ThirdPartyAccessOverviewDto | null>(null);
	readonly loading = signal(false);
	readonly busyJobId = signal<string | null>(null);
	/** Per-row vendor DDL choice, keyed by jobId (immutable updates). */
	readonly selections = signal<Record<string, string>>({});

	// ── Derivations ──
	readonly vendors = computed(() => this.overview()?.vendors ?? []);
	readonly jobs = computed(() => this.overview()?.jobs ?? []);
	readonly customerName = computed(() => this.overview()?.customerName ?? '');
	readonly hasHistory = computed(() => this.vendors().length > 0);
	readonly activeCount = computed(() => this.jobs().filter(j => j.assignment?.isActive).length);

	ngOnInit(): void {
		this.load();
	}

	selectionFor(jobId: string): string {
		// Single-vendor customers (the norm) get that vendor preselected.
		const chosen = this.selections()[jobId];
		if (chosen) return chosen;
		const vendors = this.vendors();
		return vendors.length === 1 ? vendors[0].userId : '';
	}

	onSelectVendor(jobId: string, userId: string): void {
		this.selections.set({ ...this.selections(), [jobId]: userId });
	}

	/** Grant / re-enable — single click, no dialog. */
	grant(job: ThirdPartyJobRowDto, userId: string): void {
		const vendor = this.vendors().find(v => v.userId === userId);
		if (!vendor || this.busyJobId()) return;

		this.busyJobId.set(job.jobId);
		this.accessService.grant(job.jobId, { userId: vendor.userId }).subscribe({
			next: overview => {
				this.busyJobId.set(null);
				this.overview.set(overview);
			},
			error: err => {
				this.busyJobId.set(null);
				this.toast.show(err?.error?.message ?? 'Failed to grant access.', 'danger');
			},
		});
	}

	disable(job: ThirdPartyJobRowDto): void {
		if (this.busyJobId()) return;

		this.busyJobId.set(job.jobId);
		this.accessService.disable(job.jobId).subscribe({
			next: overview => {
				this.busyJobId.set(null);
				this.overview.set(overview);
			},
			error: err => {
				this.busyJobId.set(null);
				this.toast.show(err?.error?.message ?? 'Failed to disable access.', 'danger');
			},
		});
	}

	private load(): void {
		this.loading.set(true);
		this.accessService.getOverview().subscribe({
			next: overview => {
				this.overview.set(overview);
				this.loading.set(false);
			},
			error: () => {
				this.loading.set(false);
				this.toast.show('Failed to load 3rd party access overview.', 'danger');
			},
		});
	}
}
