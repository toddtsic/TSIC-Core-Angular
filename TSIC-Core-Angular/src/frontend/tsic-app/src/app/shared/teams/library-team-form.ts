import type { ClubTeamDto } from '@core/api';

/**
 * The library-team form's rules, shared by the Edit/Add modal (team-form-modal) and the teams
 * board's inline editor, so the two can never disagree about what a valid library team is.
 */

/** Grad year options: current year through +12, plus Adult. */
export function libraryGradYearOptions(): string[] {
    const now = new Date().getFullYear();
    const years: string[] = [];
    for (let y = now; y <= now + 12; y++) years.push(String(y));
    years.push('Adult');
    return years;
}

/** The name matches another library team (case-insensitive), the row being edited excluded. */
export function isDuplicateLibraryName(
    teams: readonly ClubTeamDto[], name: string, excludeClubTeamId: number | null | undefined,
): boolean {
    const n = name.trim().toLowerCase();
    if (!n) return false;
    return teams.some(t => t.clubTeamId !== excludeClubTeamId && (t.clubTeamName ?? '').trim().toLowerCase() === n);
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
