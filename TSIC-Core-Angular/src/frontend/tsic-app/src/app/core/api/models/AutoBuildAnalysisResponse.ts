/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AutoBuildFeasibility } from './AutoBuildFeasibility';
import type { DivisionMatch } from './DivisionMatch';
export type AutoBuildAnalysisResponse = {
    sourceJobId: string;
    sourceJobName: string;
    sourceYear: string;
    sourceTotalGames: number;
    divisionMatches: Array<DivisionMatch>;
    feasibility: AutoBuildFeasibility;
};

