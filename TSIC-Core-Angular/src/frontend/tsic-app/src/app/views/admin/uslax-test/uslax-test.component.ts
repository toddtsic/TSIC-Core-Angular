import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { JobService } from '@infrastructure/services/job.service';
import { UsLaxValidationService, type UsLaxMember } from '@infrastructure/services/uslax-validation.service';

@Component({
	selector: 'app-uslax-test',
	standalone: true,
	imports: [FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-test.component.html',
	styleUrl: './uslax-test.component.scss'
})
export class UsLaxTestComponent {
	private readonly usLaxService = inject(UsLaxValidationService);
	private readonly jobService = inject(JobService);

	readonly membershipNumber = signal('');
	readonly isLoading = signal(false);
	readonly result = signal<UsLaxMember | null>(null);
	readonly errorMessage = signal<string | null>(null);
	readonly hasSearched = signal(false);

	readonly jobValidThrough = computed(() => {
		const job = this.jobService.currentJob();
		const raw = job?.usLaxNumberValidThroughDate;
		if (!raw) return null;
		const d = new Date(raw);
		return isNaN(d.getTime()) ? null : d;
	});

	readonly jobValidThroughDisplay = computed(() => {
		const d = this.jobValidThrough();
		return d ? d.toLocaleDateString('en-US') : 'Not set';
	});

	readonly isActive = computed(() => this.result()?.mem_status === 'Active');

	readonly expiryDate = computed(() => {
		const raw = this.result()?.exp_date;
		if (!raw) return null;
		const d = new Date(raw);
		return isNaN(d.getTime()) ? null : d;
	});

	readonly isExpiredForJob = computed(() => {
		const expiry = this.expiryDate();
		const jobDate = this.jobValidThrough();
		if (!expiry || !jobDate) return false;
		return expiry < jobDate;
	});

	verify(): void {
		const num = this.membershipNumber().trim();
		if (!num) return;

		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.result.set(null);
		this.hasSearched.set(true);

		this.usLaxService.verify(num).subscribe({
			next: member => {
				if (member) {
					this.result.set(member);
				} else {
					this.errorMessage.set('No member data returned. Check the number and try again.');
				}
				this.isLoading.set(false);
			},
			error: () => {
				this.errorMessage.set('Validation service temporarily unavailable. Please try again later.');
				this.isLoading.set(false);
			}
		});
	}

	clear(): void {
		this.membershipNumber.set('');
		this.result.set(null);
		this.errorMessage.set(null);
		this.hasSearched.set(false);
	}

	onKeydown(event: KeyboardEvent): void {
		if (event.key === 'Enter') {
			this.verify();
		}
	}
}
