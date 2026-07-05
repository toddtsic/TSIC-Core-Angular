/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultRoleMetadataSet } from './AdultRoleMetadataSet';
export type AdultProfileMigrationResult = {
    profile: string;
    displayName: string;
    success: boolean;
    jobsAffected: number;
    usLaxJobsAffected: number;
    affectedJobIds: Array<string>;
    affectedJobNames: Array<string>;
    affectedJobYears: Array<string>;
    generatedMetadata: (null | AdultRoleMetadataSet);
    generatedMetadataUsLax?: (null | AdultRoleMetadataSet);
    warnings: Array<string>;
    errorMessage: string | null;
};

