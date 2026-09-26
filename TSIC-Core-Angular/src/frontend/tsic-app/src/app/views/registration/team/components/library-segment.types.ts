import type { AgeGroupDto, ClubTeamDto } from '@core/api';

/** One registration the Club Team Library segment asks the Teams step to make. */
export interface LibraryRegisterRequest {
    team: ClubTeamDto;
    ageGroupId: string;
    /** The event's level of play — the rep's pick, else the library team's own. */
    levelOfPlay: string;
}

const WAITLIST_PREFIX = /^\s*WAITLIST\s*-?\s*/i;

/** A WAITLIST twin mirrors its parent age group: "WAITLIST - 2030". */
export function isWaitlistAgeGroup(ag: Pick<AgeGroupDto, 'ageGroupName'>): boolean {
    return WAITLIST_PREFIX.test(ag.ageGroupName);
}

/** The age group as the rep reads it — a WAITLIST twin shows its parent's name. */
export function ageGroupLabel(ag: Pick<AgeGroupDto, 'ageGroupName'>): string {
    return ag.ageGroupName.replace(WAITLIST_PREFIX, '').trim() || ag.ageGroupName;
}
