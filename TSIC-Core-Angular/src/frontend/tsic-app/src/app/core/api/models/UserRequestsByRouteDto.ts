/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UserRequestRoleDto } from './UserRequestRoleDto';
import type { UserRouteRowDto } from './UserRouteRowDto';
import type { UserRouteTotalDto } from './UserRouteTotalDto';
export type UserRequestsByRouteDto = {
    windowDays: number;
    jobCount: number;
    roles: Array<UserRequestRoleDto>;
    role?: string | null;
    rows: Array<UserRouteRowDto>;
    totals: Array<UserRouteTotalDto>;
    totalRequests: number;
    failedRequests: number;
    totalPeople: number;
    shellRequests: number;
    usageLoggingAvailable: boolean;
};

