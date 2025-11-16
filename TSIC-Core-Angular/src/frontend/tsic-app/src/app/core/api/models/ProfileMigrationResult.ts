/* generated using openapi-typescript-codegen -- do no edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProfileMetadata } from './ProfileMetadata';
export type ProfileMigrationResult = {
    profileType?: string | null;
    success?: boolean;
    fieldCount?: number;
    jobsAffected?: number;
    affectedJobIds?: Array<string> | null;
    affectedJobNames?: Array<string> | null;
    affectedJobYears?: Array<string> | null;
    generatedMetadata?: ProfileMetadata;
    warnings?: Array<string> | null;
    errorMessage?: string | null;
};

