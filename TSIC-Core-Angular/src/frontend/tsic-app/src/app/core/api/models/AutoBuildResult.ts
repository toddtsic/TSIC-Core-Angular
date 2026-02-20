/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AutoBuildDivisionResult } from './AutoBuildDivisionResult';
export type AutoBuildResult = {
    totalDivisions: number;
    divisionsScheduled: number;
    divisionsSkipped: number;
    totalGamesPlaced: number;
    gamesFailedToPlace: number;
    divisionResults: Array<AutoBuildDivisionResult>;
};

