import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import { ThirdPartyAccessService } from './services/third-party-access.service';
import type { ThirdPartyAccessOverviewDto, ThirdPartyJobRowDto, ThirdPartyVendorDto } from '@core/api';

/** The two identity fields both vendor-carrying DTOs share. */
type VendorIdentity = { userName: string; displayName: string };

/**
 * "3rd Party Data Access" (SU + SuperDirector): the customer's own console for
 * managing which authorized third party may pull its rosters/schedule export.
 * Reuse-only — only accounts that have already held export access with this
 * organization are offered; first-time logins are not created here.
 * Grant = create-or-reactivate; disable = one click. Both are single-click —
 * the standing on-page warning carries the minors'-data caution.
 *
 * The vendor identity is NEVER behind a collapsed control. Releasing minors' data
 * to a named outside agency is the whole point of the screen, so the row states who
 * that agency is before the click, not after it: one vendor (the norm) renders as
 * plain text, 2+ render as radios. A one-option <select> hid exactly the fact the
 * director most needed to read.
 */
@Component({
	selector: 'app-third-party-access',
	standalone: true,
	imports: [CommonModule],
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
	/** Per-row vendor radio choice, keyed by jobId (immutable updates). */
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

	/**
	 * The row's only vendor, when there is exactly one — the norm for a reuse-only
	 * console. Drives the no-control render: with nothing to choose, the row states the
	 * identity outright instead of burying it in a one-option picker. Per row, because a
	 * row offers only its own customer's vendor history.
	 */
	soleVendorFor(job: ThirdPartyJobRowDto): ThirdPartyVendorDto | null {
		return job.vendors.length === 1 ? job.vendors[0] : null;
	}

	/**
	 * Name leads — it is the identity a director recognizes and can hold accountable;
	 * the login is a machine detail. Vendor accounts can carry blank First/Last, so the
	 * login is promoted to the lead line rather than rendering an empty one.
	 */
	primaryLabel(v: VendorIdentity): string {
		return v.displayName || v.userName;
	}

	/** Null once the login has been promoted above — never print it twice. */
	secondaryLabel(v: VendorIdentity): string | null {
		return v.displayName ? v.userName : null;
	}

	selectionFor(job: ThirdPartyJobRowDto): string {
		// Single-vendor rows (the norm) get that vendor preselected.
		const chosen = this.selections()[job.jobId];
		if (chosen) return chosen;
		return this.soleVendorFor(job)?.userId ?? '';
	}

	onSelectVendor(jobId: string, userId: string): void {
		this.selections.set({ ...this.selections(), [jobId]: userId });
	}

	/** Grant / re-enable — single click, no dialog. */
	grant(job: ThirdPartyJobRowDto, userId: string): void {
		// Re-Activate passes the row's own assignment, which may predate its vendor history.
		const known = job.assignment?.userId === userId || job.vendors.some(v => v.userId === userId);
		if (!known || this.busyJobId()) return;

		this.busyJobId.set(job.jobId);
		this.accessService.grant(job.jobId, { userId }).subscribe({
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
