/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsageStatsPerJobRowDto } from './UsageStatsPerJobRowDto';
export type UsageStatsPerJobDto = {
    rows: Array<UsageStatsPerJobRowDto>;
    windowDays: number;
    botsExcluded: boolean;
    totalRequests: number;
    totalJobs: number;
    otherJobCount: number;
    otherRequests: number;
    usageLoggingAvailable: boolean;
};

