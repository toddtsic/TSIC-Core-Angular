import { Component, ChangeDetectionStrategy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { StoreService } from '../../../infrastructure/services/store.service';
import { FormFieldDataService } from '../../../infrastructure/services/form-field-data.service';
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

	/**
	 * The same state list every other address form in the app uses. Legacy's walk-up form bound
	 * State to a `<select>` from `ViewBag.listStates`; ours was a two-character free-text box,
	 * which is how "CA", "Cal" and "california" end up in one column.
	 */
	readonly states = inject(FormFieldDataService).getOptionsForDataSource('states');

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

	/**
	 * Legacy's `StoreWalkUpRegistrationDto` data annotations, kept: every field required, a real
	 * email, phone exactly ten digits, ZIP `12345` or `12345-6789`. This form mints a real user,
	 * family and registration, so a junk phone or ZIP is a permanent row, not a bad form post.
	 * The same three rules are enforced server-side — this is the courtesy copy.
	 */
	private static readonly EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
	private static readonly PHONE_10 = /^[0-9]{10}$/;
	private static readonly ZIP = /^\d{5}(-\d{4})?$/;

	readonly emailError = computed(() =>
		this.email().trim() && !StoreWalkUpComponent.EMAIL.test(this.email().trim())
			? 'Enter a valid email address.' : null);

	readonly phoneError = computed(() =>
		this.phone().trim() && !StoreWalkUpComponent.PHONE_10.test(this.phone().trim())
			? 'Enter a 10-digit phone number, digits only.' : null);

	readonly zipError = computed(() =>
		this.zip().trim() && !StoreWalkUpComponent.ZIP.test(this.zip().trim())
			? 'Enter a ZIP as 12345 or 12345-6789.' : null);

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
		) && !this.emailError() && !this.phoneError() && !this.zipError();
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
