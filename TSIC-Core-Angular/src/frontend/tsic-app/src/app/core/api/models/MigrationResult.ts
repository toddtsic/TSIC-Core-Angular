/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMetadata } from './ProfileMetadata';
export type MigrationResult = {
    jobId: string;
    jobName: string;
    profileType: string;
    success: boolean;
    errorMessage: string | null;
    warnings: Array<string>;
    fieldCount: number;
    generatedMetadata: (null | ProfileMetadata);
};

