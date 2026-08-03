import {
	ChangeDetectionStrategy, Component, OnInit, inject, input, signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { JobCloneService } from '../../job-clone/services/job-clone.service';
import { ToastService } from '@shared-ui/toast.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { environment } from '@environments/environment';
import type { DevUndoStatusResponse } from '@core/api';

/**
 * Sandbox-only "delete this job" — the cascade delete that used to live on the
 * post-clone release page. It sits here because this is where the operator is:
 * after a clone you land in the new job, and a throwaway job is deleted from the
 * job's own settings rather than from a URL you have to remember.
 *
 * Four gates, only one of which is this component:
 *   1. Role        — the endpoints are SuperUserOnly.
 *   2. Environment — the API 404s both of them unless IsSandbox() (= not Production).
 *   3. Predicate   — CanUndo is false the moment ANY of these exist: a non-admin
 *                    registration, a registration-accounting row, or a row in any
 *                    job-scoped table outside the clone manifest. One real
 *                    registration disqualifies a job permanently.
 *   4. TOCTOU      — the predicate re-runs inside the delete transaction.
 *
 * This renders NOTHING unless the server says CanUndo. A greyed-out Delete on a
 * live job is an invitation to find out how to un-grey it, and the "why not" text
 * is only worth showing where a yes was plausible.
 */
@Component({
	selector: 'app-job-delete-panel',
	standalone: true,
	imports: [ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-delete-panel.component.html',
	styles: [`
		.danger-zone {
			margin-top: var(--space-6);
			padding: var(--space-4);
			border: 1px solid var(--bs-danger-border-subtle, var(--bs-border-color));
			border-radius: var(--radius-md);
			background: var(--bs-danger-bg-subtle, var(--bs-tertiary-bg));
		}
		.danger-zone__title {
			display: flex;
			align-items: center;
			gap: var(--space-2);
			margin: 0 0 var(--space-2) 0;
			font-size: var(--font-size-sm);
			font-weight: var(--font-weight-semibold);
			color: var(--bs-danger-text-emphasis, var(--brand-text));
		}
		.danger-zone__hint {
			margin: 0 0 var(--space-3) 0;
			font-size: var(--font-size-xs);
			color: var(--brand-text-muted);
		}
		.danger-zone__counts {
			margin: 0 0 var(--space-3) 0;
			padding-left: var(--space-5);
			font-size: var(--font-size-xs);
			color: var(--brand-text-muted);
		}
		.danger-zone__btn {
			padding: var(--space-2) var(--space-4);
			border: 1px solid var(--bs-danger);
			border-radius: var(--radius-sm);
			background: transparent;
			color: var(--bs-danger);
			font-size: var(--font-size-sm);
			font-weight: var(--font-weight-semibold);
			cursor: pointer;
			transition: background 0.15s, color 0.15s;
		}
		.danger-zone__btn:hover:not(:disabled) { background: var(--bs-danger); color: var(--bs-body-bg); }
		.danger-zone__btn:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
		.danger-zone__btn:disabled { opacity: 0.6; cursor: not-allowed; }
		@media (prefers-reduced-motion: reduce) {
			.danger-zone__btn { transition: none; }
		}
	`],
})
export class JobDeletePanelComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly toast = inject(ToastService);
	private readonly router = inject(Router);

	readonly jobId = input.required<string>();

	// Mirrors the server's IsSandbox() (everything that is not Production). NOT
	// environment.production — that is an Angular build-optimization flag and is
	// true on staging, which is a box where this is meant to work.
	readonly isSandbox = environment.envName !== 'production';

	readonly status = signal<DevUndoStatusResponse | null>(null);
	readonly confirmOpen = signal(false);
	readonly isDeleting = signal(false);

	ngOnInit(): void {
		if (!this.isSandbox) return;
		this.cloneService.getDevUndoStatus(this.jobId()).subscribe({
			next: s => this.status.set(s),
			error: () => { /* sandbox-only endpoint; silent when it 404s */ },
		});
	}

	openConfirm(): void { this.confirmOpen.set(true); }
	cancelConfirm(): void { this.confirmOpen.set(false); }

	confirmDelete(): void {
		this.isDeleting.set(true);
		this.cloneService.deleteClonedJob(this.jobId()).subscribe({
			next: () => {
				this.isDeleting.set(false);
				this.confirmOpen.set(false);
				// The session is scoped to the job that just vanished, so every
				// job-scoped route is now a dead end. Role-selection is the app's own
				// "pick where to work" screen; 'tsic' is the jobless prefix its
				// siblings use (header switchRole, the ToS fallback).
				this.toast.show('Job deleted. Select a job to continue.', 'success', 5000);
				this.router.navigate(['/tsic/role-selection']);
			},
			error: err => {
				this.isDeleting.set(false);
				this.confirmOpen.set(false);
				this.toast.show(err.error?.message ?? 'Delete failed.', 'danger', 5000);
			},
		});
	}
}
