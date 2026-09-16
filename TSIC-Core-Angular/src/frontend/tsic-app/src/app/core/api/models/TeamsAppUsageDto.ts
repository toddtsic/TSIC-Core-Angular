/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamsAppUsageFrequencyBandDto } from './TeamsAppUsageFrequencyBandDto';
import type { TeamsAppUsageTeamRowDto } from './TeamsAppUsageTeamRowDto';
export type TeamsAppUsageDto = {
    usageLoggingAvailable: boolean;
    teamsAppEnabled: boolean;
    windowDays: number;
    daysCovered: number;
    loggingStartedAt?: string | null;
    activeUsers: number;
    rosteredUsers: number;
    returningUsers: number;
    teamsReached: number;
    teamsTotal: number;
    daysWithActivity: number;
    playersActive: number;
    playersRostered: number;
    staffActive: number;
    staffRostered: number;
    offRosterUsers: number;
    frequency: Array<TeamsAppUsageFrequencyBandDto>;
    teams: Array<TeamsAppUsageTeamRowDto>;
};

