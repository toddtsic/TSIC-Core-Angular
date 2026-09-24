import type { ClubTeamDto } from '@core/api';

/**
 * The ONE place that says what a club rep may do to a Club Team Library row and why not.
 *
 * Three surfaces show the same kebab — the library page, the wizard's library fly-in and the
 * Teams step's last-line guards behind the fly-in's emits — and each used to carry its own copy
 * of these rules. They drifted once (delete gated on "scheduled" in one place, "registered" in
 * another). Now a rule changes here and every menu changes with it.
 *
 * Each function returns the reason the action is locked, or null when it is allowed. The reason
 * is the exact text the menu shows under the greyed item. The backend is the real chokepoint
 * (TeamRegistrationService.UpdateClubTeamAsync / DeleteClubTeamAsync refuse on the same facts);
 * these mirror it so a rep never clicks a door the server slams.
 */

/**
 * THE DISTINCTION (Todd 2026-09-24, "must be absolutely clear"):
 *
 *   The LIBRARY row — name, grad year, level of play on the club's list — is the rep's. No director
 *   toggle reaches it and no event history freezes it. Every event registration took its own copy
 *   at registration time, so a library edit rewrites nothing in any job's schedule, past or future.
 *
 *   The EVENT copy — the name, age group and level of play that appear in THIS job's schedule — is
 *   the director's domain. That is what Allow Edit / Allow Add / Allow Delete govern, and those
 *   rules live with the Teams step, not here.
 *
 * The old "one team through time" locks (edit frozen once scheduled) guarded an identity model the
 * library no longer promises: "you want to rename it in your library, go ahead, have at it."
 */

/** What the rules need to know beyond the row itself. */
export interface ClubTeamLockContext {
    /** The row has a live registration (registered or waitlisted) for the event the rep is signed in to. */
    registeredHere: boolean;
    /**
     * How the reasons name the current event: "this event" inside the wizard, "the Fall Rodeo 2026"
     * on the library page, where "here" would be the very ambiguity that page exists to remove.
     */
    eventLabel: string;
}

/**
 * Edit (name, grad year, level of play — one dialog, library only): never locked. Todd's rename
 * ruling of 2026-08-18 extended to all three on 2026-09-24; UpdateClubTeamAsync dropped its
 * schedule refusal the same day. There is no separate Rename on the library side any more, and
 * no library-to-event propagation: the event copy is renamed on the Teams step. The function
 * stays so every caller keeps asking one place; the `team` argument stays for the day a rule
 * needs it.
 */
export function clubTeamEditLockReason(_team: ClubTeamDto, _ctx: ClubTeamLockContext): string | null {
    return null;
}

/**
 * Archive is a visibility flag: the team leaves the active list for the Archived section and
 * Restore brings it back. Nothing is destroyed, so schedule history is NOT a gate here (it was,
 * and it stranded every team that had registered for an event but never been put on a game).
 * Frontend-only: ArchiveClubTeamAsync has no registered check.
 */
export function clubTeamArchiveLockReason(ctx: ClubTeamLockContext): string | null {
    if (ctx.registeredHere) return `Registered for ${ctx.eventLabel}`;
    return null;
}

/**
 * Gated on bHasEventRegistrations — the SAME fact DeleteClubTeamAsync refuses on (any Teams row,
 * any job). It was gated on bHasBeenScheduled, which is narrower: a team that registered for an
 * event but never reached a game read as deletable and was rejected by the server, with a message
 * naming an event the rep could not see. The registeredHere check behind it is a belt-and-braces
 * read of the same fact from this event's own registered list, in case the flag is stale.
 */
export function clubTeamDeleteLockReason(team: ClubTeamDto, ctx: ClubTeamLockContext): string | null {
    if (team.bHasEventRegistrations) return 'Use Archive — registered for an event';
    if (ctx.registeredHere) return `Registered for ${ctx.eventLabel}`;
    return null;
}
