/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsersByRoleRowDto } from './UsersByRoleRowDto';
export type UsersByRoleDto = {
    windowDays: number;
    botsExcluded: boolean;
    jobCount: number;
    rows: Array<UsersByRoleRowDto>;
    customerUsers: number;
    adminUsers: number;
    usageLoggingAvailable: boolean;
};

