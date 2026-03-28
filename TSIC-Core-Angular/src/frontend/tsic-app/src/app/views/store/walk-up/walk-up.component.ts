import { Component, ChangeDetectionStrategy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { StoreService } from '../../../infrastructure/services/store.service';
import type { StoreWalkUpRegisterRequest, JobPulseDto } from '@core/api';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { environment } from '@environments/environment';

@Component({
	selector: 'app-walk-up',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './walk-up.component.html',
	styleUrl: './walk-up.component.scss',
})
export class StoreWalkUpComponent implements OnInit {
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly http = inject(HttpClient);
	private readonly store = inject(StoreService);
	private readonly auth = inject(AuthService);

	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);

	/** Whether walk-up is confirmed allowed (page won't render until verified) */
	readonly ready = signal(false);

	// Form fields
	readonly firstName = signal('');
	readonly lastName = signal('');
	readonly email = signal('');
	readonly phone = signal('');
	readonly streetAddress = signal('');
	readonly city = signal('');
	readonly state = signal('');
	readonly zip = signal('');

	ngOnInit(): void {
		const jp = this.jobPath;
		if (!jp) {
			this.ready.set(true);
			return;
		}

		this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jp}/pulse`).subscribe({
			next: pulse => {
				if (!pulse.allowStoreWalkup) {
					// Walk-up disabled → redirect to store login
					this.router.navigate(['../login'], { relativeTo: this.route, replaceUrl: true });
				} else {
					this.ready.set(true);
				}
			},
			error: () => {
				// Pulse unavailable — allow access, backend will gate the POST
				this.ready.set(true);
			},
		});
	}

	private get jobPath(): string {
		// jobPath lives on the :jobPath parent route, not this segment
		let snapshot = this.route.snapshot;
		while (snapshot) {
			const jp = snapshot.paramMap.get('jobPath');
			if (jp) return jp;
			snapshot = snapshot.parent!;
		}
		return '';
	}

	get isValid(): boolean {
		return !!(
			this.firstName().trim() &&
			this.lastName().trim() &&
			this.email().trim() &&
			this.phone().trim() &&
			this.streetAddress().trim() &&
			this.city().trim() &&
			this.state().trim() &&
			this.zip().trim()
		);
	}

	submit(): void {
		if (!this.isValid || this.isLoading()) return;

		this.isLoading.set(true);
		this.errorMessage.set(null);

		const request: StoreWalkUpRegisterRequest = {
			jobPath: this.jobPath,
			firstName: this.firstName().trim(),
			lastName: this.lastName().trim(),
			email: this.email().trim(),
			phone: this.phone().trim(),
			streetAddress: this.streetAddress().trim(),
			city: this.city().trim(),
			state: this.state().trim(),
			zip: this.zip().trim(),
		};

		this.store.walkUpRegister(request).subscribe({
			next: response => {
				this.auth.applyTokenPair(response.accessToken, response.refreshToken);
				this.isLoading.set(false);
				this.router.navigate(['../'], { relativeTo: this.route });
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Registration failed. Please try again.');
				this.isLoading.set(false);
			},
		});
	}
}
