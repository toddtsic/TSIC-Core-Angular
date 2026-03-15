import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { isStoreEligible } from '@infrastructure/constants/roles.constants';
import { LoginComponent } from '@views/auth/login/login.component';
import { environment } from '@environments/environment';
import type { JobPulseDto } from '@core/api';

/**
 * Focused store login page — shown when an unauthenticated (or non-eligible)
 * user attempts to access the store.
 *
 * Pulse-aware:
 *   If the job has active player registration → show Family Sign In + Guest walk-up
 *   If the job has NO player registration   → redirect straight to walk-up (no accounts exist)
 *
 * Layout: two-card side-by-side (same pattern as TeamLoginStepComponent):
 *   Left  — embedded LoginComponent themed for family sign-in
 *   Right — "No Account?" card with link to walk-up guest registration
 */
@Component({
	selector: 'app-store-login',
	standalone: true,
	imports: [LoginComponent, RouterLink],
	templateUrl: './login.component.html',
	styleUrl: './login.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreLoginComponent implements OnInit {
	private readonly auth = inject(AuthService);
	private readonly http = inject(HttpClient);
	private readonly router = inject(Router);
	private readonly route = inject(ActivatedRoute);

	readonly returnUrl = signal('');

	/** Whether the page is ready to render (pulse loaded) */
	readonly ready = signal(false);

	/** Computed returnUrl to pass to the embedded LoginComponent */
	readonly storeReturnUrl = computed(() => {
		const url = this.returnUrl();
		if (url) return url;
		const jp = this.jobPath;
		return jp ? `/${jp}/store` : '';
	});

	private get jobPath(): string {
		let snapshot = this.route.snapshot;
		while (snapshot) {
			const jp = snapshot.paramMap.get('jobPath');
			if (jp) return jp;
			snapshot = snapshot.parent!;
		}
		return '';
	}

	ngOnInit(): void {
		this.returnUrl.set(this.route.snapshot.queryParamMap.get('returnUrl') || '');
		this.checkPulse();
	}

	/** Fetch job pulse to decide if family login is relevant */
	private checkPulse(): void {
		const jp = this.jobPath;
		if (!jp) {
			this.ready.set(true);
			return;
		}

		this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jp}/pulse`).subscribe({
			next: pulse => {
				if (!pulse.playerRegistrationOpen) {
					// No player registration → no family accounts exist → skip to walk-up
					this.router.navigate(['../walk-up'], { relativeTo: this.route, replaceUrl: true });
				} else {
					this.ready.set(true);
				}
			},
			error: () => {
				// Pulse unavailable — show the login page as a safe fallback
				this.ready.set(true);
			},
		});
	}

	/** Watch for successful login — navigate away once a store-eligible role is active */
	private readonly _authNav = effect(() => {
		const user = this.auth.currentUser();
		if (user && this.auth.isAuthenticated() && isStoreEligible(user.role)) {
			const target = this.returnUrl() || this.storeReturnUrl();
			if (target) {
				this.router.navigateByUrl(target);
			}
		}
	});
}
