/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MigrationResult } from './MigrationResult';
export type MigrationReport = {
    successCount?: number;
    failureCount?: number;
    warningCount?: number;
    skippedCount?: number;
    startedAt?: string;
    completedAt?: string | null;
    results?: Array<MigrationResult> | null;
    globalWarnings?: Array<string> | null;
};

