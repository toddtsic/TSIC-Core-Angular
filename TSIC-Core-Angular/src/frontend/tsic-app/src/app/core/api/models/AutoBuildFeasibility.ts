/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PoolSizePattern } from './PoolSizePattern';
export type AutoBuildFeasibility = {
    totalCurrentDivisions: number;
    coveredDivisions: number;
    uncoveredDivisions: number;
    nameMatchedDivisions: number;
    confidenceLevel: string;
    confidencePercent: number;
    fieldMismatches: Array<string>;
    warnings: Array<string>;
    availablePatterns: Array<PoolSizePattern>;
};

