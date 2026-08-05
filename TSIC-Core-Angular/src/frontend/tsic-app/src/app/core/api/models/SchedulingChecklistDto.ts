/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChecklistAgegroupStepDto } from './ChecklistAgegroupStepDto';
import type { ChecklistDivisionStepDto } from './ChecklistDivisionStepDto';
import type { ChecklistPairingsStepDto } from './ChecklistPairingsStepDto';
import type { ChecklistPoolsStepDto } from './ChecklistPoolsStepDto';
import type { ChecklistRulesStepDto } from './ChecklistRulesStepDto';
import type { ChecklistScheduleStatsDto } from './ChecklistScheduleStatsDto';
export type SchedulingChecklistDto = {
    pools: ChecklistPoolsStepDto;
    dates: ChecklistAgegroupStepDto;
    fields: ChecklistDivisionStepDto;
    rules: ChecklistRulesStepDto;
    pairings: ChecklistPairingsStepDto;
    gameCount: number;
    buildUnlocked: boolean;
    scheduleStats: ChecklistScheduleStatsDto;
};

