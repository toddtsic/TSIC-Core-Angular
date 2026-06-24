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
    /**
     * Optional pulse field indicating at least one team is currently within its
     * registration-availability window. Player flow sets this to
     * 'playerTeamsAvailableForRegistration'. When present, closure on EITHER flag
     * is treated as "not open".
     */
    teamsAvailableKey?: string;
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
        const teamsBlocked = !!config.teamsAvailableKey && !pulse[config.teamsAvailableKey];
        if (!pulse[config.registrationOpenKey] || teamsBlocked) {
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
    teamsAvailableKey: 'playerTeamsAvailableForRegistration',
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

/**
 * Adult self-registration guard (coach / referee / college recruiter).
 *
 * Unlike player/team, the adult wizard is role-keyed via ?role=<key>, and each role
 * has its own director release gate surfaced as a distinct pulse flag. There is no
 * invite-token concept for adults — these are open self-registrations once released.
 * A closed role redirects anonymous viewers to the landing with a clear message; the
 * backend (ResolveAdultRole) enforces the same gate as a defense-in-depth backstop.
 */
const ADULT_ROLE_GATES: Record<string, { flag: string; label: string }> = {
    coach: { flag: 'staffRegistrationOpen', label: 'Coach' },
    referee: { flag: 'refereeRegistrationOpen', label: 'Referee' },
    recruiter: { flag: 'recruiterRegistrationOpen', label: 'College recruiter' },
};

export const adultRegistrationGuard: CanActivateFn = async (route, state) => {
    const http = inject(HttpClient);
    const auth = inject(AuthService);
    const router = inject(Router);
    const toast = inject(ToastService);

    const role = (route.queryParamMap.get('role') || '').toLowerCase();
    const gate = ADULT_ROLE_GATES[role];
    // Unknown/missing role — let the wizard surface its own "role required" error.
    if (!gate) return true;

    // Returning, authenticated adults keep access (manage their reg / pay balance),
    // mirroring the player/team guard's authenticated passthrough.
    if (auth.isAuthenticated()) return true;

    let jobPath = route.paramMap.get('jobPath') || route.parent?.paramMap.get('jobPath');
    if (!jobPath && state.url) {
        const match = state.url.match(/^\/([a-z0-9-]{3,40})(\/|$|\?)/);
        if (match) jobPath = match[1];
    }
    jobPath = jobPath || 'tsic';

    let pulse: Record<string, unknown>;
    try {
        pulse = await firstValueFrom(
            http.get<Record<string, unknown>>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
        );
    } catch {
        return true; // Pulse unavailable — wizard will show its own error
    }

    if (!pulse[gate.flag]) {
        toast.show(`${gate.label} registration is not currently open for this event.`, 'warning', 6000);
        return router.createUrlTree([`/${jobPath}`]);
    }
    return true;
};
