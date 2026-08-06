import type { AgeGroupDto } from '@core/api';

/**
 * One event age-group "slot" with all presentation flags derived. Shared shape
 * between the library fly-in's compact chip row and the add-and-register form's
 * fee-bearing card grid so the recommendation + occupancy logic can never drift
 * apart again (it previously had to be fixed in both places at once).
 */
export interface AgeGroupSlot {
    ageGroupId: string;
    ageGroupName: string;
    /** Deposit + balance — only surfaced by the fee-bearing (pill) variant. */
    fee: number;
    spotsLeft: number;
    isFull: boolean;
    isAlmostFull: boolean;
    /** Grad-year best match (after WAITLIST-twin redirect) — the highlighted/pre-selected slot. */
    isRecommended: boolean;
}

const isWaitlist = (name: string): boolean => name.toUpperCase().startsWith('WAITLIST');

/** First 20xx year embedded anywhere in a name ("2029 Boys" -> 2029), else null. */
const parseGradYear = (name: string | null | undefined): number | null => {
    const match = (name ?? '').match(/(20\d{2})/);
    return match ? Number(match[1]) : null;
};

/**
 * The oldest (numerically smallest) grad year this event offers, or null when the
 * event is not grad-year-named and the whole concept doesn't apply.
 *
 * Detection is STRICT: every non-waitlist age group must yield a 20xx year. A
 * single non-year group ("HS Open", "U14") disables it, and deliberately so —
 * the threshold below is only meaningful if every offering is year-named. With a
 * mixed set, an older team might legitimately belong in the non-year group, and
 * demoting it would be wrong. Decoration is tolerated ("2029 Boys" parses),
 * only genuinely non-year names opt the event out.
 *
 * WAITLIST twins are excluded: they mirror a parent group rather than widening
 * what the event offers, so they can neither enable nor move the threshold.
 */
export function resolveOldestOfferedGradYear(ageGroups: readonly AgeGroupDto[]): number | null {
    const offered = ageGroups.filter(a => !isWaitlist(a.ageGroupName));
    if (offered.length === 0) return null;

    let oldest: number | null = null;
    for (const ag of offered) {
        const year = parseGradYear(ag.ageGroupName);
        if (year === null) return null;
        if (oldest === null || year < oldest) oldest = year;
    }
    return oldest;
}

/**
 * Can this team be placed in ANY age group this event offers?
 *
 * Playing UP is allowed (ruling: Todd, 08-06), so a team may enter any group
 * whose grad year is at or below its own — a 2030 team can play the 2029 or 2028
 * age group. That reduces eligibility to one threshold: the team is offered
 * something iff its grad year is not older than the event's oldest age group.
 * Playing DOWN (an older team in a younger age group) is what this excludes.
 *
 * Returns TRUE whenever the answer is unknown — event not grad-year-named, or
 * the team has no parseable grad year. A data gap must never bury a valid team;
 * the cost of a false "offered" is a row in the wrong group, the cost of a false
 * "not offered" is a rep who cannot find their team.
 */
export function isTeamOfferedAtEvent(
    oldestOfferedGradYear: number | null,
    teamGradYear: string | null | undefined,
): boolean {
    if (oldestOfferedGradYear === null) return true;
    const gy = parseGradYear(teamGradYear);
    if (gy === null) return true;
    return gy >= oldestOfferedGradYear;
}

/**
 * Best-match age group by team grad year — exact match first, then a non-waitlist
 * substring match. When the match is full (oversubscribed), redirect to its
 * WAITLIST twin so the rep lands on the waitlist rather than a full age group.
 * The server auto-mints the twin the instant an age group fills (parity with the
 * player roster hook), so it is normally present; if it isn't yet, fall back to
 * the full parent and the server waitlists on submit.
 *
 * Returns '' when there's no grad year or no match (caller leaves the picker
 * unselected so the rep must choose).
 */
export function resolveRecommendedAgeGroupId(
    ageGroups: readonly AgeGroupDto[],
    gradYear: string | null | undefined,
): string {
    const gy = gradYear ?? '';
    if (!gy || ageGroups.length === 0) return '';

    const matched =
        ageGroups.find(a => a.ageGroupName === gy) ??
        ageGroups.find(a => a.ageGroupName.includes(gy) && !isWaitlist(a.ageGroupName));
    if (!matched) return '';

    if (matched.registeredCount >= matched.maxTeams) {
        const twinName = `WAITLIST - ${matched.ageGroupName}`.toUpperCase();
        const twin = ageGroups.find(a => a.ageGroupName.toUpperCase() === twinName);
        if (twin) return twin.ageGroupId;
    }
    return matched.ageGroupId;
}

/**
 * Map age groups to presentation slots with occupancy + recommendation flags.
 * The single source of the chip/pill grid for both the library fly-in and the
 * add-and-register form.
 */
export function buildAgeGroupSlots(
    ageGroups: readonly AgeGroupDto[],
    gradYear: string | null | undefined,
): AgeGroupSlot[] {
    const recommendedId = resolveRecommendedAgeGroupId(ageGroups, gradYear);
    return ageGroups.map(ag => {
        const spotsLeft = Math.max(0, ag.maxTeams - ag.registeredCount);
        return {
            ageGroupId: ag.ageGroupId,
            ageGroupName: ag.ageGroupName,
            fee: (ag.deposit || 0) + (ag.balanceDue || 0),
            spotsLeft,
            isFull: spotsLeft === 0,
            isAlmostFull: spotsLeft > 0 && spotsLeft <= 2,
            isRecommended: ag.ageGroupId === recommendedId,
        };
    });
}
