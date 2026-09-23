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
