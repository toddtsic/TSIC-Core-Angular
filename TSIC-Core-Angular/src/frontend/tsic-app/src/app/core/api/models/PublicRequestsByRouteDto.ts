/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PublicRouteRowDto } from './PublicRouteRowDto';
import type { PublicRouteTotalDto } from './PublicRouteTotalDto';
export type PublicRequestsByRouteDto = {
    windowDays: number;
    jobCount: number;
    rows: Array<PublicRouteRowDto>;
    totals: Array<PublicRouteTotalDto>;
    totalRequests: number;
    failedRequests: number;
    usageLoggingAvailable: boolean;
};

