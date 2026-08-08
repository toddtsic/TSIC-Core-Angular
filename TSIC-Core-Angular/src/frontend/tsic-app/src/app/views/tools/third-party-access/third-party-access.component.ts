import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { ThirdPartyAccessService } from './services/third-party-access.service';
import type { ThirdPartyAccessOverviewDto, ThirdPartyJobRowDto, ThirdPartyVendorDto } from '@core/api';

/**
 * "3rd Party Data Access" (SU + SuperDirector): the customer's own console for
 * managing which authorized third party may pull its rosters/schedule export.
 * Reuse-only — the vendor picker offers only accounts that have already held
 * export access with this organization; first-time logins are not created here.
 * Grant = create-or-reactivate behind a deliberate typed confirmation (minors'
 * data leaves the organization's control); disable = one click, no friction.
 */
@Component({
	selector: 'app-third-party-access',
	standalone: true,
	imports: [CommonModule, FormsModule, ConfirmDialogComponent],
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
	/** Pending grant awaiting the typed confirmation, or null. */
	readonly confirmTarget = signal<{ job: ThirdPartyJobRowDto; vendor: ThirdPartyVendorDto } | null>(null);

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

	/** Grant / re-enable both route through the typed confirmation. */
	requestGrant(job: ThirdPartyJobRowDto, userId: string): void {
		const vendor = this.vendors().find(v => v.userId === userId);
		if (!vendor) return;
		this.confirmTarget.set({ job, vendor });
	}

	confirmGrant(): void {
		const target = this.confirmTarget();
		if (!target || this.busyJobId()) return;

		this.confirmTarget.set(null);
		this.busyJobId.set(target.job.jobId);
		this.accessService.grant(target.job.jobId, { userId: target.vendor.userId }).subscribe({
			next: overview => {
				this.busyJobId.set(null);
				this.overview.set(overview);
				this.toast.show(
					`${target.vendor.userName} now has data access on '${target.job.jobName}'.`, 'success');
			},
			error: err => {
				this.busyJobId.set(null);
				this.toast.show(err?.error?.message ?? 'Failed to grant access.', 'danger');
			},
		});
	}

	cancelGrant(): void {
		this.confirmTarget.set(null);
	}

	disable(job: ThirdPartyJobRowDto): void {
		if (this.busyJobId()) return;

		this.busyJobId.set(job.jobId);
		this.accessService.disable(job.jobId).subscribe({
			next: overview => {
				this.busyJobId.set(null);
				this.overview.set(overview);
				this.toast.show(`Third-party access disabled on '${job.jobName}'.`, 'success');
			},
			error: err => {
				this.busyJobId.set(null);
				this.toast.show(err?.error?.message ?? 'Failed to disable access.', 'danger');
			},
		});
	}

	confirmMessage(): string {
		const target = this.confirmTarget();
		if (!target) return '';
		return `
			<p><strong>You are permitting an outside agency to access data about minors.</strong></p>
			<p><strong>${target.vendor.userName}</strong> will be able to sign in to
			<strong>${target.job.jobName}</strong> and download player rosters — including
			player and parent names and email addresses — plus the full event schedule.</p>
			<ul>
				<li>Rosters are released <strong>only</strong> for age groups you have flagged
					<em>Third-Party Roster Access</em> (Teams &amp; Rosters &rarr; L-A-D-T Editor).</li>
				<li>Once downloaded, this data leaves your control. Review your organization's
					privacy policy before proceeding.</li>
			</ul>`;
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
