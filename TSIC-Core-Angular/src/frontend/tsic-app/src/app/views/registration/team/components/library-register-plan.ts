import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { normalizeLop } from '@shared/teams/lop-choices';
import { describeSlotPricing, isTeamOfferedAtEvent, resolveRecommendedAgeGroupId, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, isWaitlistAgeGroup, type LibraryRegisterRequest } from './library-segment.types';

/**
 * What pressing a library team's Register button does right now, resolved from the library team
 * and this event's age groups. ONE rule for every surface that registers from the library (the
 * Club Team Library segment and the Register-a-team modal), so they can never disagree about
 * what a team's button says.
 *
 * `ready` is the one-press case: the library already knows the level of play and the grad year
 * names exactly one age group, so the button can say where the team is going. Anything short of
 * both is a choice for the rep, never a guess on their behalf.
 */
export type RegisterPlan =
    | { kind: 'registered' }
    | { kind: 'ready'; req: LibraryRegisterRequest; ageGroupLabel: string; waitlist: boolean; pricing: SlotPricing }
    /** A real choice: no LOP saved; no exact age group but the team can play up; or older than every age group here. */
    | { kind: 'choose'; why: 'lop' | 'playUp' | 'outside' }
    | { kind: 'closed' };

export interface RegisterPlanContext {
    ageGroups: readonly AgeGroupDto[];
    /** resolveOldestOfferedGradYear(ageGroups) — passed in so a list computes it once. */
    oldestOffered: number | null;
    /** Team registration open AND the director allows adds. */
    canRegister: boolean;
}

/** Club order: the 20xx grad year, or +Infinity when there is none (sorts last). */
export function gradKey(team: ClubTeamDto): number {
    const m = (team.clubTeamGradYear ?? '').match(/(20\d{2})/);
    return m ? Number(m[1]) : Number.POSITIVE_INFINITY;
}

/** By grad year, then name. */
export function byGradYearThenName(a: ClubTeamDto, b: ClubTeamDto): number {
    return gradKey(a) - gradKey(b) || a.clubTeamName.localeCompare(b.clubTeamName);
}

/** Can this team be placed in some age group here (playing up allowed)? */
export function fitsEvent(team: ClubTeamDto, ctx: Pick<RegisterPlanContext, 'oldestOffered'>): boolean {
    return isTeamOfferedAtEvent(ctx.oldestOffered, team.clubTeamGradYear);
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

/**
 * The plan: LOP from the library row (must be on the 1–5 scale), age group from the grad year
 * (redirected to the WAITLIST twin when full — resolveRecommendedAgeGroupId's rule).
 */
export function planRegistration(team: ClubTeamDto, reg: RegisteredTeamDto | null, ctx: RegisterPlanContext): RegisterPlan {
    if (reg) return { kind: 'registered' };
    if (!ctx.canRegister || team.bArchived) return { kind: 'closed' };
    const lop = normalizeLop(team.clubTeamLevelOfPlay);
    if (!lop) return { kind: 'choose', why: 'lop' };
    const fits = fitsEvent(team, ctx);
    const ageGroupId = fits ? resolveRecommendedAgeGroupId(ctx.ageGroups, team.clubTeamGradYear) : '';
    const ag = ageGroupId ? ctx.ageGroups.find(a => a.ageGroupId === ageGroupId) : undefined;
    if (!ag) return { kind: 'choose', why: fits ? 'playUp' : 'outside' };
    const waitlist = ageGroupWaitlists(ag);
    return {
        kind: 'ready',
        req: { team, ageGroupId: ag.ageGroupId, levelOfPlay: lop },
        ageGroupLabel: ageGroupLabel(ag),
        waitlist,
        pricing: waitlist ? { kind: 'waitlist' } : pricingOfAgeGroup(ag),
    };
}
