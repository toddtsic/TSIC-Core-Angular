import type { ClubTeamDto } from '@core/api';

/**
 * The library-team form's rules, shared by the Edit/Add modal (team-form-modal) and the teams
 * board's inline editor, so the two can never disagree about what a valid library team is.
 */

/**
 * No grad year — a real answer, never a blank (Todd 2026-09-28). Stored as-is, so it reads right
 * everywhere it prints and passes every "grad year required" check. Like Adult: no year to sort
 * by or to suggest an age group from.
 */
export const GRAD_YEAR_NA = 'N/A';

/** Grad year options: current year through +12, plus Adult and N/A. */
export function libraryGradYearOptions(): string[] {
    const now = new Date().getFullYear();
    const years: string[] = [];
    for (let y = now; y <= now + 12; y++) years.push(String(y));
    years.push('Adult', GRAD_YEAR_NA);
    return years;
}

/** Case- and spacing-insensitive compare for library names and grad years. */
export function sameLibraryText(a: string | null | undefined, b: string | null | undefined): boolean {
    return (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase();
}

/**
 * Another library team already IS this one: same name AND grad year (case-insensitive), the row
 * being edited excluded. Name + grad year is the library's identity — the server's own rule
 * (ClubTeams FindByIdentityAsync) — so "2029 Blue · 2029" and "2029 Blue · 2030" are two teams.
 */
export function isDuplicateLibraryName(
    teams: readonly ClubTeamDto[], name: string, gradYear: string | null | undefined,
    excludeClubTeamId: number | null | undefined,
): boolean {
    if (!name.trim()) return false;
    return teams.some(t => t.clubTeamId !== excludeClubTeamId
        && sameLibraryText(t.clubTeamName, name) && sameLibraryText(t.clubTeamGradYear, gradYear));
}

function yearToken(name: string): string | null {
    return /\b\d{4}\b/.exec(name)?.[0] ?? null;
}

/**
 * The two edits that almost always mean a different squad, on a row whose history would then be
 * inherited: the grad year moved, or the four-digit year in the name moved. Either alone fires it
 * (reps forget the grad year field). A name with no year, or a year appearing where there was
 * none, is a rename, not a new team. Never without event history. A hint, never a gate.
 */
export function looksLikeDifferentTeam(team: ClubTeamDto, newName: string, newGradYear: string): boolean {
    if (!team.bHasEventRegistrations) return false;
    const gradMoved = !!team.clubTeamGradYear && !!newGradYear && team.clubTeamGradYear !== newGradYear;
    const was = yearToken(team.clubTeamName);
    const now = yearToken(newName);
    return gradMoved || (!!was && !!now && was !== now);
}
