import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { JobService } from '@infrastructure/services/job.service';
import { environment } from '@environments/environment';

interface UsLaxOutput {
	membership_id: string;
	mem_status: string;
	exp_date: string;
	firstname: string;
	lastname: string;
	birthdate: string;
	gender: string;
	age_verified: string;
	email: string;
	postalcode: string;
	state: string;
	involvement: string[];
}

interface UsLaxResponse {
	status_code: number;
	output: UsLaxOutput | null;
}

@Component({
	selector: 'app-uslax-test',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './uslax-test.component.html',
	styleUrl: './uslax-test.component.scss'
})
export class UsLaxTestComponent {
	private readonly http = inject(HttpClient);
	private readonly jobService = inject(JobService);
	private readonly apiUrl = environment.apiUrl;

	readonly membershipNumber = signal('');
	readonly isLoading = signal(false);
	readonly result = signal<UsLaxOutput | null>(null);
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

		this.http.get<UsLaxResponse>(`${this.apiUrl}/validation/uslax`, {
			params: { number: num }
		}).subscribe({
			next: res => {
				if (res?.output) {
					this.result.set(res.output);
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
