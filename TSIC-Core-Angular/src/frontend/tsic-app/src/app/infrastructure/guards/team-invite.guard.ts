import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';

/**
 * Guard for the registration/team route.
 *
 * Checks two conditions in order:
 * 1. BRegistrationAllowTeam — is team registration open at all?
 * 2. BTeamRegRequiresToken — does this job require a club rep invite token?
 *
 * If a token is required:
 * - User must be authenticated (Phase 1 login at minimum)
 * - The `?invite=` query param must contain a valid source RegistrationId
 * - Validate endpoint confirms userId + same customer
 */
export const teamInviteGuard: CanActivateFn = async (route, state) => {
	const http = inject(HttpClient);
	const auth = inject(AuthService);
	const router = inject(Router);
	const toast = inject(ToastService);

	// Resolve jobPath from route hierarchy
	let jobPath = route.paramMap.get('jobPath') || route.parent?.paramMap.get('jobPath');
	if (!jobPath && state.url) {
		const match = state.url.match(/^\/([a-z0-9-]{3,40})(\/|$|\?)/);
		if (match) jobPath = match[1];
	}
	jobPath = jobPath || 'tsic';

	// Fetch job pulse — anonymous, always works
	let pulse: any;
	try {
		pulse = await firstValueFrom(
			http.get<any>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
		);
	} catch {
		// If pulse fetch fails, allow through — wizard will show its own error state
		return true;
	}

	// 1. Team registration not open at all
	if (!pulse.teamRegistrationOpen) {
		toast.show('Team registration is not currently open for this event.', 'warning', 6000);
		return router.createUrlTree([`/${jobPath}`]);
	}

	// 2. No token required — open registration, proceed
	if (!pulse.teamRegRequiresToken) {
		return true;
	}

	// 3. Token required — must be authenticated (Phase 1 minimum)
	if (!auth.isAuthenticated()) {
		return router.createUrlTree([`/${jobPath}/login`], {
			queryParams: { returnUrl: state.url }
		});
	}

	// 4. Validate invite token against the server
	const user = auth.getCurrentUser()!;
	const inviteRegId = route.queryParamMap.get('invite');

	if (!inviteRegId || !user.userId) {
		toast.show('Only club reps with valid invitations may register teams for this event.', 'danger', 6000);
		return router.createUrlTree([`/${jobPath}`]);
	}

	let result: { allowed: boolean };
	try {
		result = await firstValueFrom(
			http.get<{ allowed: boolean }>(`${environment.apiUrl}/team-invite/validate`, {
				params: {
					targetJobPath: jobPath,
					sourceRegId: inviteRegId,
					userId: user.userId
				}
			})
		);
	} catch {
		toast.show('Could not verify your invitation. Please try again.', 'danger', 5000);
		return router.createUrlTree([`/${jobPath}`]);
	}

	if (!result.allowed) {
		toast.show('Only club reps with valid invitations may register teams for this event.', 'danger', 6000);
		return router.createUrlTree([`/${jobPath}`]);
	}

	return true;
};
