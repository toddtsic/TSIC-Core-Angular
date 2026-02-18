import { Component, inject, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobCloneService } from './services/job-clone.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
	JobCloneSourceDto,
	JobCloneResponse,
	CloneSummary,
} from '@core/api';

@Component({
	selector: 'app-job-clone',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-clone.component.html',
	styleUrl: './job-clone.component.scss',
})
export class JobCloneComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly toast = inject(ToastService);

	// ── Source data ──
	readonly sourceJobs = signal<JobCloneSourceDto[]>([]);
	readonly selectedSourceId = signal<string>('');
	readonly isLoadingSources = signal(false);

	// ── Target fields ──
	jobPathTarget = '';
	jobNameTarget = '';
	yearTarget = '';
	seasonTarget = '';
	displayName = '';
	leagueNameTarget = '';
	expiryAdmin = '';
	expiryUsers = '';
	regFormFrom = '';

	// ── Flags ──
	upAgegroupNamesByOne = true;
	setDirectorsToInactive = true;
	noParallaxSlide1 = false;

	// ── Result state ──
	readonly isCloning = signal(false);
	readonly result = signal<JobCloneResponse | null>(null);
	readonly error = signal<string | null>(null);

	ngOnInit(): void {
		this.loadSources();
	}

	private loadSources(): void {
		this.isLoadingSources.set(true);
		this.cloneService.getSources().subscribe({
			next: sources => {
				this.sourceJobs.set(sources);
				this.isLoadingSources.set(false);
			},
			error: err => {
				this.toast.show('Failed to load source jobs', 'danger', 4000);
				this.isLoadingSources.set(false);
				console.error('Failed to load source jobs', err);
			},
		});
	}

	onSourceSelected(): void {
		const sourceId = this.selectedSourceId();
		if (!sourceId) return;

		const source = this.sourceJobs().find(j => j.jobId === sourceId);
		if (!source) return;

		// Smart defaults: increment year in path and name
		const currentYear = source.year ?? '';
		const nextYear = currentYear ? String(Number(currentYear) + 1) : '';

		this.jobPathTarget = currentYear && nextYear
			? source.jobPath.replace(currentYear, nextYear)
			: source.jobPath + '-copy';

		this.jobNameTarget = currentYear && nextYear
			? (source.jobName ?? '').replace(currentYear, nextYear)
			: (source.jobName ?? '') + ' (Copy)';

		this.yearTarget = nextYear || currentYear;
		this.seasonTarget = source.season ?? '';
		this.displayName = source.displayName ?? '';
		this.leagueNameTarget = this.jobNameTarget;

		// Default expiry: +1 year from now
		const oneYearOut = new Date();
		oneYearOut.setFullYear(oneYearOut.getFullYear() + 1);
		this.expiryAdmin = this.toDateInput(oneYearOut);
		this.expiryUsers = this.toDateInput(oneYearOut);

		// Reset result
		this.result.set(null);
		this.error.set(null);
	}

	onClone(): void {
		if (!this.selectedSourceId() || !this.jobPathTarget || !this.jobNameTarget) {
			this.error.set('Please fill in all required fields.');
			return;
		}

		this.isCloning.set(true);
		this.error.set(null);
		this.result.set(null);

		this.cloneService.cloneJob({
			sourceJobId: this.selectedSourceId(),
			jobPathTarget: this.jobPathTarget,
			jobNameTarget: this.jobNameTarget,
			yearTarget: this.yearTarget,
			seasonTarget: this.seasonTarget,
			displayName: this.displayName,
			leagueNameTarget: this.leagueNameTarget,
			expiryAdmin: this.expiryAdmin,
			expiryUsers: this.expiryUsers,
			regFormFrom: this.regFormFrom || undefined,
			upAgegroupNamesByOne: this.upAgegroupNamesByOne,
			setDirectorsToInactive: this.setDirectorsToInactive,
			noParallaxSlide1: this.noParallaxSlide1,
		}).subscribe({
			next: response => {
				this.result.set(response);
				this.isCloning.set(false);
				this.toast.show(`Job cloned: ${response.newJobPath}`, 'success');
			},
			error: err => {
				const message = err.error?.message ?? 'Clone failed. Please check the parameters.';
				this.error.set(message);
				this.isCloning.set(false);
				this.toast.show(message, 'danger', 4000);
			},
		});
	}

	private toDateInput(d: Date): string {
		return d.toISOString().slice(0, 10);
	}
}
