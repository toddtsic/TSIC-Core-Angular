import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import type { StoreWalkUpRegisterRequest } from '@core/api';
import { AuthService } from '../../../infrastructure/services/auth.service';

@Component({
	selector: 'app-walk-up',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './walk-up.component.html',
	styleUrl: './walk-up.component.scss',
})
export class StoreWalkUpComponent {
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly store = inject(StoreService);
	private readonly auth = inject(AuthService);

	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// Form fields
	readonly firstName = signal('');
	readonly lastName = signal('');
	readonly email = signal('');
	readonly phone = signal('');
	readonly streetAddress = signal('');
	readonly city = signal('');
	readonly state = signal('');
	readonly zip = signal('');

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
