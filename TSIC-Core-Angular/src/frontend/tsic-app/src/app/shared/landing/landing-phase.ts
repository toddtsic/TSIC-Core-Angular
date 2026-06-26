import type { JobPulseDto } from '@core/api';

/**
 * The event's lifecycle position, derived from FACTS (schedule released + the
 * first/last game dates vs now), NOT from the director's registration toggles —
 * which are routinely left on after an event ends. Precedence is first-match in
 * `derivePhase` below.
 *
 * Shared by the public job-landing (status line / notices) and the Smart
 * Bulletins band (which CTAs to surface), so the two can never drift apart.
 */
export type EventPhase =
	| 'superseded'       // a live later-year sibling exists — page redirects to it
	| 'concluded'        // schedule published AND the last game day has fully passed
	| 'inSeason'         // schedule published AND the first game day has arrived
	| 'preEvent'         // schedule published, first game still in the future
	| 'registrationOpen' // registration (player/team) accepting, or a club rep with teams
	| 'planned'          // registration announced/planned but not yet open
	| 'preview';         // nothing actionable yet

/**
 * Which CTA keys are *eligible* in each phase. A candidate is built only when its
 * own data flag is set (store has items, schedule published, …); this set then
 * removes the ones that don't belong in the lifecycle stage. That filter is what
 * lets a finished event ignore a Register toggle a director left on.
 */
export const CTAS_BY_PHASE: Record<EventPhase, ReadonlySet<string>> = {
	superseded: new Set(),
	preview: new Set(),
	planned: new Set(['store']),
	concluded: new Set(['pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters']),
	// Player/coach/referee/recruiter registration is NOT gated by schedule
	// publication — publishing the schedule must not hide those cards. Team
	// registration (register-team) is intentionally absent: it stays gated and
	// vanishes once the schedule goes public. Each still requires its own pulse
	// flag, so these only appear when the director actually has that reg open.
	inSeason: new Set(['register-player', 'register-coach', 'register-referee', 'register-recruiter', 'my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	preEvent: new Set(['register-player', 'register-coach', 'register-referee', 'register-recruiter', 'my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	registrationOpen: new Set(['register-player', 'my-registration', 'pay-balance', 'register-team', 'my-teams', 'register-coach', 'register-referee', 'register-recruiter', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance'])
};

/** Local start-of-day (calendar-day comparisons, not raw 24h spans). */
export function startOfDay(d: Date): Date {
	const x = new Date(d);
	x.setHours(0, 0, 0, 0);
	return x;
}

/**
 * Resolve the lifecycle phase from the live pulse. Pure: same pulse + same day
 * always yields the same phase. First match wins — a finished event (concluded)
 * or an in-progress one (inSeason) outranks the registration flags, so a Register
 * toggle left on after the last game can't resurrect a "Register Player" card.
 */
export function derivePhase(p: JobPulseDto | null, now: Date): EventPhase {
	if (!p) return 'preview';
	if (p.supersededByLaterEvent) return 'superseded';
	const today = startOfDay(now).getTime();
	const lastGame = p.lastGameDate ? startOfDay(new Date(p.lastGameDate)).getTime() : null;
	// Concluded = released AND the last game day has fully passed (strict <, so the
	// last game day itself still reads in-season).
	if (p.schedulePublished && lastGame !== null && lastGame < today) return 'concluded';
	const firstGame = p.firstGameDate ? startOfDay(new Date(p.firstGameDate)).getTime() : null;
	if (p.schedulePublished && firstGame !== null && firstGame <= today) return 'inSeason';
	if (p.schedulePublished) return 'preEvent';
	if (p.playerRegistrationOpen || p.teamRegistrationOpen || (p.myClubRepTeamCount ?? 0) > 0) return 'registrationOpen';
	if (p.playerRegistrationPlanned || p.adultRegistrationPlanned || p.playerRegOpensSoonest != null) return 'planned';
	return 'preview';
}
