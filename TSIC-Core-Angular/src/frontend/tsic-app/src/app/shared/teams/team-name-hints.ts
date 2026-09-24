/**
 * Soft guidance for club-team names. Shared by the two library-team forms so the
 * two never disagree about what a "thin" name is.
 *
 * The naming tip ("enter 2028 Blue, not Atlantic County Wave 2028 Blue") was taken
 * literally by real clubs: libraries full of teams named "2028", "2029", "2030" —
 * name == grad year, so the rep's own grid reads Team 2028 | Grad 2028 and two
 * squads in one year are indistinguishable. This nudges; it never blocks
 * (ruling: Todd, 2026-09-22). A bare year is a legal name.
 */

/** True when the name is nothing but a 20xx year (whitespace tolerated). */
export function isBareYearName(name: string | null | undefined): boolean {
    return /^\s*20\d{2}\s*$/.test(name ?? '');
}

/**
 * Words a club name shares with half the sport. A team name may carry these without it meaning
 * the rep typed their club name in — "Elite" is a level, "North" is a side of town.
 */
const GENERIC_CLUB_WORDS = new Set([
    'lacrosse', 'lax', 'club', 'team', 'teams', 'elite', 'select', 'premier', 'united', 'academy',
    'athletic', 'athletics', 'sports', 'sport', 'boys', 'girls', 'youth', 'national', 'north',
    'south', 'east', 'west', 'central', 'county', 'city', 'state', 'travel', 'program', 'the',
]);

/**
 * Is the club name in the team name? Schedules print "{club}:{team}" (ScheduleRepository.
 * ComposeTeamLabel), so a club name inside the team name reads Club:Club 2028 Blue.
 *   'full'    — the whole club name is in there (the forms BLOCK on this)
 *   'partial' — a distinctive word of the club name is in there, as a whole word (warn only)
 *   null      — clean
 */
export function clubNameInTeamName(clubName: string | null | undefined, teamName: string | null | undefined): 'full' | 'partial' | null {
    const club = (clubName ?? '').trim().toLowerCase();
    const name = (teamName ?? '').trim().toLowerCase();
    if (!club || !name) return null;
    if (name.includes(club)) return 'full';
    const nameWords = new Set(name.split(/[^a-z0-9]+/).filter(Boolean));
    const distinctive = club.split(/[^a-z0-9]+/).filter(w => w.length >= 4 && !GENERIC_CLUB_WORDS.has(w));
    return distinctive.some(w => nameWords.has(w)) ? 'partial' : null;
}

/** The schedule label exactly as ScheduleRepository.ComposeTeamLabel prints it. */
export function scheduleTeamLabel(clubName: string | null | undefined, teamName: string): string {
    const club = (clubName ?? '').trim();
    return club ? `${club}:${teamName}` : teamName;
}
