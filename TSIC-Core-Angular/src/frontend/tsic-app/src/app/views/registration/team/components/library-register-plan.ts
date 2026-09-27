import type { AgeGroupDto, ClubTeamDto } from '@core/api';
import { describeSlotPricing, type SlotPricing } from './event-age-group.util';
import { isWaitlistAgeGroup } from './library-segment.types';

/**
 * Shared rules for the surfaces that list library teams against this event (the Club Team Library
 * segment and the Register-a-team modal), so they never disagree.
 *
 * No age-group eligibility test lives here any more (Todd 2026-09-27, "let the club reps decide"):
 * any library team may be registered into any age group; the modal only PRESELECTS a guess.
 */

/** Club order: the 20xx grad year, or +Infinity when there is none (sorts last). */
export function gradKey(team: ClubTeamDto): number {
    const m = (team.clubTeamGradYear ?? '').match(/(20\d{2})/);
    return m ? Number(m[1]) : Number.POSITIVE_INFINITY;
}

/** By grad year, then name. */
export function byGradYearThenName(a: ClubTeamDto, b: ClubTeamDto): number {
    return gradKey(a) - gradKey(b) || a.clubTeamName.localeCompare(b.clubTeamName);
}

/** Registering into this age group would waitlist the team: a WAITLIST twin, or a full group. */
export function ageGroupWaitlists(ag: AgeGroupDto): boolean {
    return isWaitlistAgeGroup(ag) || ag.registeredCount >= ag.maxTeams;
}

/** Phase-honest price of an open slot in this age group. */
export function pricingOfAgeGroup(ag: AgeGroupDto): SlotPricing {
    return describeSlotPricing({
        isFull: false,
        fee: (ag.deposit || 0) + (ag.balanceDue || 0),
        deposit: ag.deposit || 0,
        balanceDue: ag.balanceDue || 0,
        fullPaymentRequired: !!ag.fullPaymentRequired,
    });
}
