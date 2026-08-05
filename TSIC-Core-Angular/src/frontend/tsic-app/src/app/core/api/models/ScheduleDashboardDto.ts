/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChecklistScheduleStatsDto } from './ChecklistScheduleStatsDto';
import type { GamesPerDayDto } from './GamesPerDayDto';
export type ScheduleDashboardDto = {
    stats: ChecklistScheduleStatsDto;
    schedulableDivisionCount: number;
    teamsScheduled: number;
    activeTeamCount: number;
    gamesPerDay: Array<GamesPerDayDto>;
};

