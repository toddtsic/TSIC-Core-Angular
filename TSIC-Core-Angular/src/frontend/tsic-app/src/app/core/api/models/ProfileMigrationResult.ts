/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMetadata } from './ProfileMetadata';
export type ProfileMigrationResult = {
    profileType: string;
    success: boolean;
    fieldCount: number;
    jobsAffected: number;
    affectedJobIds: Array<string>;
    affectedJobNames: Array<string>;
    affectedJobYears: Array<string>;
    generatedMetadata: (null | ProfileMetadata);
    warnings: Array<string>;
    errorMessage: string | null;
};

