/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMigrationResult } from './ProfileMigrationResult';
export type ProfileBatchMigrationReport = {
    startedAt?: string;
    completedAt?: string | null;
    totalProfiles?: number;
    successCount?: number;
    failureCount?: number;
    totalJobsAffected?: number;
    results?: Array<ProfileMigrationResult> | null;
    globalWarnings?: Array<string> | null;
};

