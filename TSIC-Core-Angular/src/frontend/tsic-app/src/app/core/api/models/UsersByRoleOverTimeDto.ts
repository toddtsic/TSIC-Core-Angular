/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsersByRoleBucketRowDto } from './UsersByRoleBucketRowDto';
export type UsersByRoleOverTimeDto = {
    bucket: string;
    since: string;
    buckets: Array<string>;
    firstRecordedAt: string | null;
    jobCount: number;
    rows: Array<UsersByRoleBucketRowDto>;
    usageLoggingAvailable: boolean;
};

