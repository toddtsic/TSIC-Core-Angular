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
