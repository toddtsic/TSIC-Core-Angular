import type { AgeGroupDto } from '@core/api';

/** Safely coerce to number (handles string/null/undefined from API). */
function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

/**
 * Filter out "dropped" age groups and full "waitlist" groups,
 * then sort: available first, full at bottom, waitlist last, alphabetical within.
 */
export function filterAndSortAgeGroups(ageGroups: AgeGroupDto[]): AgeGroupDto[] {
    return ageGroups
        .filter(ag => {
            const name = ag.ageGroupName.toLowerCase();
            if (name.startsWith('dropped')) return false;
            if (name.startsWith('waitlist')) {
                return toNumber(ag.maxTeams) - toNumber(ag.registeredCount) > 0;
            }
            return true;
        })
        .sort((a, b) => {
            const aName = a.ageGroupName.toLowerCase();
            const bName = b.ageGroupName.toLowerCase();
            const aFull = toNumber(a.registeredCount) >= toNumber(a.maxTeams) && !aName.startsWith('waitlist');
            const bFull = toNumber(b.registeredCount) >= toNumber(b.maxTeams) && !bName.startsWith('waitlist');
            const aWaitlist = aName.startsWith('waitlist');
            const bWaitlist = bName.startsWith('waitlist');
            if (aFull && !bFull) return 1;
            if (!aFull && bFull) return -1;
            if (aWaitlist && !bWaitlist) return 1;
            if (!aWaitlist && bWaitlist) return -1;
            return a.ageGroupName.localeCompare(b.ageGroupName);
        });
}
