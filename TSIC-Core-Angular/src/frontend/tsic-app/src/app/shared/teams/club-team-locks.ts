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

/** What the rules need to know beyond the row itself. */
export interface ClubTeamLockContext {
    /** The row has a live registration (registered or waitlisted) for the event the rep is signed in to. */
    registeredHere: boolean;
    /** The director's Allow Edit for this event, AND team registration is open. */
    canEdit: boolean;
    /**
     * How the reasons name the current event: "this event" inside the wizard, "the Fall Rodeo 2026"
     * on the library page, where "here" would be the very ambiguity that page exists to remove.
     */
    eventLabel: string;
}

/**
 * Renaming a library entry has NO lock at all — not event history, and NOT the director's Allow
 * Edit toggle. The library is the rep's own cross-event list and renaming in it is their decision,
 * independent of any one event (Todd's ruling, 2026-08-18); one director switching Allow Edit off
 * must not lock a rep out of a list that isn't about that event. The rename dialog's opt-in "use it
 * for this event too" IS an event write, and that half still answers to the toggle.
 */
export function clubTeamRenameLockReason(): string | null {
    return null;
}

/**
 * Details = grad year + level of play (and the name, pre-registration). Those carry the squad's
 * identity, so they stay locked once the team has played. Name alone goes through Rename.
 * Mirrors UpdateClubTeamAsync, which refuses a scheduled team.
 */
export function clubTeamEditLockReason(team: ClubTeamDto, ctx: ClubTeamLockContext): string | null {
    if (!ctx.canEdit) return 'Editing closed by the director';
    if (team.bHasBeenScheduled) return 'Has event history';
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
