/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsageAnalysisJobDto } from './UsageAnalysisJobDto';
export type UsageAnalysisScopeDto = {
    scope: string;
    maxScope: string;
    usageLoggingAvailable: boolean;
    currentJobIsLive: boolean;
    jobs: Array<UsageAnalysisJobDto>;
};

