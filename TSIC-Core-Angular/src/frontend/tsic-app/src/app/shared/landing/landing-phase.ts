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
	| 'concluded'        // the backend's eventConcluded answer — the ONLY source of "event is over"
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
	// Registration cards are NOT gated by schedule publication — publishing the schedule
	// must not hide them. This now INCLUDES team registration: per director discretion,
	// the "Register Team" card stays governed by the director's toggle (+ team fees),
	// rather than auto-vanishing once the schedule goes public (it used to be special-cased
	// to disappear in these phases; that override was removed 2026-06-28). Each card still
	// requires its own pulse flag, so it only appears when the director actually has that reg
	// open — and the eventConcluded door still hides everything once the event is truly over.
	inSeason: new Set(['register-player', 'register-team', 'register-coach', 'register-referee', 'register-recruiter', 'my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	preEvent: new Set(['register-player', 'register-team', 'register-coach', 'register-referee', 'register-recruiter', 'my-registration', 'pay-balance', 'my-teams', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance']),
	registrationOpen: new Set(['register-player', 'my-registration', 'pay-balance', 'register-team', 'my-teams', 'register-coach', 'register-referee', 'register-recruiter', 'view-schedule', 'store', 'rosters', 'player-insurance', 'team-insurance'])
};

/** Local start-of-day (calendar-day comparisons, not raw 24h spans). */
export function startOfDay(d: Date): Date {
	const x = new Date(d);
	x.setHours(0, 0, 0, 0);
	return x;
}

/**
 * Canonical "a player can register right now" predicate — the SINGLE source of truth shared
 * by derivePhase (below), the registration-panel cards, and the player wizard's
 * `registrationClosed` gate. The invite guard enforces the same rule (by pulse-key config).
 *
 * Player registration is TEAM-LEVEL: a player registers ONTO a team, valid only while that
 * team is within its registration-availability window (Teams.Effectiveasofdate..Expireondate),
 * surfaced as `playerTeamsAvailableForRegistration`. The job-level toggle being on is NOT
 * enough — with no team in-window the wizard shows "registration closed", so a card/phase that
 * keys off the toggle alone would disagree with the wizard (e.g. a showcase whose team windows
 * have all passed). Routing every landing consumer through this one function keeps them in lockstep.
 */
export function isPlayerRegistrationEffectivelyOpen(p: JobPulseDto | null): boolean {
	return !!p && p.playerRegistrationOpen && p.playerTeamsAvailableForRegistration;
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
	// Concluded is the SERVER's call (pulse.eventConcluded) — computed once on the server clock
	// over the published-lastGameDate → EventEndDate → ExpiryUsers hierarchy, the SAME predicate
	// the write authority enforces. The FE consumes the bit rather than recomputing from raw
	// dates on the client clock (that two-clock recompute drifted at the day boundary).
	if (p.eventConcluded) return 'concluded';
	const today = startOfDay(now).getTime();
	const firstGame = p.firstGameDate ? startOfDay(new Date(p.firstGameDate)).getTime() : null;
	if (p.schedulePublished && firstGame !== null && firstGame <= today) return 'inSeason';
	if (p.schedulePublished) return 'preEvent';
	// No schedule published → residual zone. "Concluded" is NOT decided here: it is the
	// backend's call alone (pulse.eventConcluded, checked above). The frontend must never
	// re-derive "event is over" from raw dates, the event year, or participation — doing so
	// lets the page contradict the backend authority. The signals we read here are the live
	// registration flags — player reg via the canonical predicate (toggle AND a team in-window)
	// so the phase never disagrees with the cards/wizard that share the same function.
	if (isPlayerRegistrationEffectivelyOpen(p) || p.teamRegistrationOpen || (p.myClubRepTeamCount ?? 0) > 0) return 'registrationOpen';
	if (p.playerRegistrationPlanned || p.adultRegistrationPlanned || p.playerRegOpensSoonest != null) return 'planned';
	return 'preview';
}
