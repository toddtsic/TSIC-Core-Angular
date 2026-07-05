/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultProfileMigrationResult } from './AdultProfileMigrationResult';
export type AdultProfileBatchMigrationReport = {
    startedAt: string;
    completedAt: string | null;
    totalProfiles: number;
    successCount: number;
    failureCount: number;
    totalJobsAffected: number;
    results: Array<AdultProfileMigrationResult>;
    globalWarnings: Array<string>;
};

