/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DivisionStrategyEntry } from './DivisionStrategyEntry';
export type DivisionStrategyProfileResponse = {
    strategies: Array<DivisionStrategyEntry>;
    source: string;
    inferredFromJobId?: string | null;
    inferredFromJobName?: string | null;
    disconnects?: any[] | null;
};

