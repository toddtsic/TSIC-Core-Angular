/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMigrationResult } from './ProfileMigrationResult';
export type ProfileBatchMigrationReport = {
    startedAt?: string;
    completedAt?: string | null;
    totalProfiles?: number | string;
    successCount?: number | string;
    failureCount?: number | string;
    totalJobsAffected?: number | string;
    results?: Array<ProfileMigrationResult>;
    globalWarnings?: Array<string>;
};

