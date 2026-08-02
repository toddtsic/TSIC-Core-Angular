import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { JobCloneService } from './services/job-clone.service';
import { ToastService } from '@shared-ui/toast.service';
import type { SuspendedJobDto } from '@core/api';

/**
 * Job-clone suite entry point:
 *   - Clone current job  → workbench (clone flavor)
 *   - Blank job          → workbench (blank flavor)
 *   - Unreleased jobs    → release/:jobId (the list makes the release flow re-reachable
 *     after navigating away — no history.state dependency).
 */
@Component({
	selector: 'app-job-clone-landing',
	standalone: true,
	imports: [CommonModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-clone-landing.component.html',
	styleUrls: ['./job-clone-shared.scss', './job-clone-landing.component.scss'],
})
export class JobCloneLandingComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly toast = inject(ToastService);

	readonly suspended = signal<SuspendedJobDto[]>([]);
	readonly isLoading = signal(false);

	ngOnInit(): void {
		this.isLoading.set(true);
		this.cloneService.getSuspended().subscribe({
			next: jobs => {
				this.suspended.set(jobs);
				this.isLoading.set(false);
			},
			error: () => {
				this.isLoading.set(false);
				this.toast.show('Failed to load unreleased jobs', 'danger', 4000);
			},
		});
	}

	formatDate(value: string | null | undefined): string {
		if (!value) return '—';
		const d = new Date(value);
		return isNaN(d.getTime()) ? '—' : d.toISOString().slice(0, 10);
	}
}
