/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MigrationResult } from './MigrationResult';
export type MigrationReport = {
    successCount?: number | string;
    failureCount?: number | string;
    warningCount?: number | string;
    skippedCount?: number | string;
    startedAt?: string;
    completedAt?: string | null;
    results?: Array<MigrationResult>;
    globalWarnings?: Array<string>;
};

