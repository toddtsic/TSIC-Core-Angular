/**
 * Display helpers for stored schedule team names — shared by the games grid and the team
 * results panel so a team's name reads the same everywhere it appears.
 *
 * Schedules.T1Name / T2Name (and the panel's opponent names, which come from them) are
 * denormalized as "{club}:{team}" — "Metro:2034/35 Blue", "All Lax Select:2034". Some clubs
 * then name the team after the club again, so the stored string doubles back on itself:
 * "North Bay Lacrosse Club:North Bay Lacrosse Club-Riptide". The echo is collapsed out.
 *
 * DISPLAY ONLY — title and aria-label keep the stored value, so hovering, screen readers,
 * and anyone matching against what the club typed still see it verbatim. Fixing it in the
 * data would touch every consumer of T1Name and rewrite what directors entered.
 *
 * Each part is also trimmed. Legacy rows were composed from club names carrying a trailing
 * space ("Prime Time :2029"); the write path trims now (ComposeTeamLabel in
 * ScheduleRepository), but the stored legacy text still has it.
 */

export interface TeamNameParts {
    /** Null — ONE line, the whole label in team — when there is no club to split off. */
    readonly club: string | null;
    readonly team: string;
}

/**
 * The label split at the club boundary: club on line one, team on line two. club is null
 * whenever there is no club to split off: no colon (team-name-only jobs, unresolved bracket
 * feeds like "F1 (RED#1)"), an empty club or team either side of it, or a team named
 * exactly after its club (two identical lines would say nothing new).
 */
export function splitTeamName(name: string | null | undefined, slotLabel?: string | null): TeamNameParts {
    const raw = stripSlotSuffix(name ?? '', slotLabel).trim();
    const sep = raw.indexOf(':');
    if (sep < 0) return { club: null, team: raw };
    const club = raw.slice(0, sep).trim();
    const team = raw.slice(sep + 1).trim();
    if (!club) return { club: null, team };
    if (!team) return { club: null, team: club };
    if (!team.toLowerCase().startsWith(club.toLowerCase())) return { club, team };
    // Drop the echoed club plus whatever joins it to the real name ("-", " ", "/", ":").
    const rest = team.slice(club.length).replace(/^[\s\-–—:_/|]+/, '').trim();
    return rest ? { club, team: rest } : { club: null, team: club };
}

/** One-line form: "club:team", or just the team when there is no club. */
export function teamLabel(name: string | null | undefined, slotLabel?: string | null): string {
    const p = splitTeamName(name, slotLabel);
    return p.club ? `${p.club}:${p.team}` : p.team;
}

/**
 * Legacy-written bracket rows store the slot inside the name — "Capital Lacrosse
 * Club:2029 Orange  (S1)" — and the grid's seed tag already says S1, so it read twice.
 * Drop the suffix only when it is EXACTLY the given slot label (a stale "(Q1)" on an S
 * game stays visible: that is a data fact, not a repeat). No label → name unchanged.
 * A name that is nothing but the suffix is kept.
 */
function stripSlotSuffix(name: string, slotLabel?: string | null): string {
    if (!slotLabel) return name;
    const suffix = `(${slotLabel})`;
    const trimmed = name.trimEnd();
    if (!trimmed.toUpperCase().endsWith(suffix.toUpperCase())) return name;
    const rest = trimmed.slice(0, -suffix.length).trimEnd();
    return rest || name;
}
