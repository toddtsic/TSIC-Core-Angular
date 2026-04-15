import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';

interface InviteGuardConfig {
    /** Pulse field indicating registration is open (e.g. 'playerRegistrationOpen') */
    registrationOpenKey: string;
    /** Pulse field indicating invite token is required (e.g. 'playerRegRequiresToken') */
    requiresTokenKey: string;
    /** API endpoint prefix for invite validation (e.g. 'player-invite') */
    validateEndpoint: string;
    /** Human-readable type for toast messages (e.g. 'Player', 'Team') */
    registrationType: string;
}

/**
 * Factory for registration invite guards.
 *
 * Player and team registration share identical guard logic — only the
 * pulse field names, validation endpoint, and error messages differ.
 */
export function createRegistrationInviteGuard(config: InviteGuardConfig): CanActivateFn {
    return async (route, state) => {
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
        let pulse: Record<string, unknown>;
        try {
            pulse = await firstValueFrom(
                http.get<Record<string, unknown>>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
            );
        } catch {
            return true; // Pulse unavailable — wizard will show its own error
        }

        // 1. Registration not open
        // Authenticated users (returning ClubReps / Players) keep access so they can view
        // existing teams, pay balances, and use any capabilities the job config still allows.
        // The wizard consults per-action pulse flags (ClubRepAllowAdd/Edit/Delete, etc.) to
        // gate UI, and the corresponding endpoints enforce those flags server-side.
        if (!pulse[config.registrationOpenKey]) {
            if (auth.isAuthenticated()) {
                return true;
            }
            toast.show(`${config.registrationType} registration is not currently open for this event.`, 'warning', 6000);
            return router.createUrlTree([`/${jobPath}`]);
        }

        // 2. No invite token required — open registration
        if (!pulse[config.requiresTokenKey]) {
            return true;
        }

        // 3. Token required — must be authenticated (Phase 1 minimum)
        if (!auth.isAuthenticated()) {
            return router.createUrlTree([`/${jobPath}/login`], {
                queryParams: { returnUrl: state.url }
            });
        }

        // 4. Validate invite token
        const user = auth.getCurrentUser()!;
        const inviteRegId = route.queryParamMap.get('invite');

        if (!inviteRegId || !user.userId) {
            toast.show(`Only accepted ${config.registrationType.toLowerCase()}s with valid invitations may register for this event.`, 'danger', 6000);
            return router.createUrlTree([`/${jobPath}`]);
        }

        let result: { allowed: boolean };
        try {
            result = await firstValueFrom(
                http.get<{ allowed: boolean }>(`${environment.apiUrl}/${config.validateEndpoint}/validate`, {
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
            toast.show(`Only accepted ${config.registrationType.toLowerCase()}s with valid invitations may register for this event.`, 'danger', 6000);
            return router.createUrlTree([`/${jobPath}`]);
        }

        return true;
    };
}

export const playerInviteGuard = createRegistrationInviteGuard({
    registrationOpenKey: 'playerRegistrationOpen',
    requiresTokenKey: 'playerRegRequiresToken',
    validateEndpoint: 'player-invite',
    registrationType: 'Player',
});

export const teamInviteGuard = createRegistrationInviteGuard({
    registrationOpenKey: 'teamRegistrationOpen',
    requiresTokenKey: 'teamRegRequiresToken',
    validateEndpoint: 'team-invite',
    registrationType: 'Team',
});
