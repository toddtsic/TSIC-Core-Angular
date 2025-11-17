/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMetadata } from './ProfileMetadata';
export type MigrationResult = {
    jobId: string;
    jobName?: string | null;
    profileType?: string | null;
    success?: boolean;
    errorMessage?: string | null;
    warnings?: Array<string> | null;
    fieldCount?: number;
    generatedMetadata?: ProfileMetadata;
};

