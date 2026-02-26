/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AutoBuildFeasibility } from './AutoBuildFeasibility';
import type { ConfirmedAgegroupMapping } from './ConfirmedAgegroupMapping';
import type { PoolSizeCoverage } from './PoolSizeCoverage';
export type AutoBuildAnalysisResponse = {
    sourceJobId: string;
    sourceJobName: string;
    sourceYear: string;
    sourceTotalGames: number;
    divisionCoverage: Array<PoolSizeCoverage>;
    feasibility: AutoBuildFeasibility;
    agegroupMappings: Array<ConfirmedAgegroupMapping>;
};

