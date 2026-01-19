/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamFeeUpdateDto } from './TeamFeeUpdateDto';
export type RecalculateTeamFeesResponse = {
    updatedCount: number;
    updates: Array<TeamFeeUpdateDto>;
    skippedCount: number;
    skippedReasons: Array<string>;
};

