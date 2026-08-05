/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChecklistAgegroupSlotsDto } from './ChecklistAgegroupSlotsDto';
export type ChecklistBracketStepDto = {
    hasBracketGames: boolean;
    complete: boolean;
    uncoveredSlotCount: number;
    uncoveredByAgegroup: Array<ChecklistAgegroupSlotsDto>;
};

